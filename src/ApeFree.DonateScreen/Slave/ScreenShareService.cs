using ApeFree.DonateScreen.Capture;
using ApeFree.DonateScreen.Codecs;
using ApeFree.DonateScreen.Contracts;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// 远程屏幕共享服务实现（由 Slave 节点暴露）
    /// 负责启动屏幕抓取循环、FFmpeg H.264 实时视频流编码（或 TurboJPEG 兼容编码）与事件推流。
    /// 支持基于配额的单调流控与高性能内存池复用（零 GC 压力）。
    /// </summary>
    public class ScreenShareService : IScreenShareService, IDisposable
    {
        private readonly object _syncLock = new();
        private readonly object _pushLock = new();
        private IScreenCapturer _capturer;
        private FFmpegH264Encoder? _h264Encoder;
        private Thread? _workerThread;
        private volatile bool _isRunning;
        private volatile bool _isPaused;
        private volatile bool _forceKeyFrame = true;
        private long _frameIndex;
        private CaptureOptionsDto _options = new();
        private readonly List<Rectangle> _accumulatedDirtyRects = new();

        // 刷新配额流控机制
        private int _remainingRefreshQuota;
        private readonly AutoResetEvent _quotaWakeEvent = new(false);

        public event EventHandler<ScreenFramePacketDto>? FrameArrived;
        public event EventHandler<ScreenTopologyDto>? TopologyChanged;

        public ScreenTopologyDto ScreenTopology => _capturer.Topology;

        /// <summary>
        /// 当前剩余可用刷新配额
        /// </summary>
        public int RemainingRefreshQuota => Volatile.Read(ref _remainingRefreshQuota);

        /// <summary>
        /// 当前上行实时帧率 (FPS)
        /// </summary>
        public int CurrentFps { get; private set; }

        /// <summary>
        /// 当前上行实时码率 (KB/s)
        /// </summary>
        public double CurrentBitrateKbps { get; private set; }

        /// <summary>
        /// 累计发送帧数
        /// </summary>
        public long TotalFramesSent => _frameIndex;

        /// <summary>
        /// 当前底层捕获引擎名称
        /// </summary>
        public string EngineName => $"FFmpeg H.264 ({_capturer.EngineName})";

        private ScreenShareMode _shareMode = ScreenShareMode.FullControl;
        private string _nodeName = string.Empty;

        public ScreenShareService(CaptureOptionsDto? options = null, IScreenCapturer? customCapturer = null, ScreenShareMode shareMode = ScreenShareMode.FullControl, string nodeName = "")
        {
            if (options != null) _options = options;
            _shareMode = shareMode;
            _nodeName = nodeName;
            _capturer = customCapturer ?? new DxgiScreenCapturer(_options.TargetDisplayIndex);
        }

        public ScreenTopologyDto GetScreenTopology()
        {
            return _capturer.Topology;
        }

        public SlaveNodeRegistrationDto GetRegistrationInfo()
        {
            var topo = _capturer.Topology;
            return new SlaveNodeRegistrationDto
            {
                SlaveId = string.IsNullOrWhiteSpace(_nodeName) ? Environment.MachineName : _nodeName,
                MachineName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                ShareMode = _shareMode,
                DisplayWidth = topo.Width,
                DisplayHeight = topo.Height,
                Note = _nodeName
            };
        }

        public void RequestKeyFrame()
        {
            _forceKeyFrame = true;
            _capturer.RequestKeyFrame();
            _quotaWakeEvent.Set();
        }

        /// <summary>
        /// 增加或设置屏幕画面刷新配额（流控单调递增，配额只能改大不能改小，并清空历史积压直接推最新画面）
        /// </summary>
        public void SetRefreshQuota(int newQuota)
        {
            if (newQuota <= 0) return;

            int current;
            do
            {
                current = Volatile.Read(ref _remainingRefreshQuota);
                if (newQuota <= current)
                {
                    // 只能改大，不能改小
                    return;
                }
            } while (Interlocked.CompareExchange(ref _remainingRefreshQuota, newQuota, current) != current);

            // 收到刷新的那一刻，清除掉队列积压的旧脏矩形，直接推最新全屏关键帧
            lock (_accumulatedDirtyRects)
            {
                _accumulatedDirtyRects.Clear();
            }
            RequestKeyFrame();
        }

        public void UpdateCaptureOptions(CaptureOptionsDto options)
        {
            if (options == null) return;
            lock (_syncLock)
            {
                bool displayChanged = (_options.TargetDisplayIndex != options.TargetDisplayIndex);
                _options = options;

                if (displayChanged)
                {
                    _capturer.Dispose();
                    _capturer = new DxgiScreenCapturer(_options.TargetDisplayIndex);
                    TopologyChanged?.Invoke(this, _capturer.Topology);
                    RequestKeyFrame();
                }

                _h264Encoder?.UpdateConfig(_options.MaxFps, _options.Crf);
            }
        }

        public void SetStreamingPaused(bool isPaused)
        {
            _isPaused = isPaused;
            if (!isPaused)
            {
                _quotaWakeEvent.Set();
            }
        }

        /// <summary>
        /// 启动屏幕抓取与推流循环
        /// </summary>
        public void Start()
        {
            lock (_syncLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _workerThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "ApeFree.ScreenCaptureWorker",
                    Priority = ThreadPriority.AboveNormal
                };
                _workerThread.Start();
            }
        }

        /// <summary>
        /// 停止抓取与推流循环
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }
            _quotaWakeEvent.Set();
            _workerThread?.Join(1000);
            _workerThread = null;
        }

        private void CaptureLoop()
        {
            int fpsCounter = 0;
            long bytesCounter = 0;
            var lastStatTime = DateTime.UtcNow;

            while (_isRunning)
            {
                // 流控配额检查：若剩余配额归零，说明客户端已最小化或暂停接收，立即休眠等待
                if (Volatile.Read(ref _remainingRefreshQuota) <= 0)
                {
                    _quotaWakeEvent.WaitOne(100);
                    continue;
                }

                int maxFps;
                int crf;
                int quality;
                PixelFormatMode pixelFormat;
                VideoCodecType codec;
                lock (_syncLock)
                {
                    maxFps = Math.Clamp(_options.MaxFps, 5, 60);
                    crf = Math.Clamp(_options.Crf, 15, 40);
                    quality = Math.Clamp(_options.JpegQuality, 10, 95);
                    pixelFormat = _options.PixelFormat;
                    codec = _options.Codec;
                }

                int intervalMs = Math.Max(1, 1000 / maxFps);
                var loopStart = DateTime.UtcNow;

                if (!_isPaused && FrameArrived != null)
                {
                    TryCaptureAndSendFrame(maxFps, crf, quality, pixelFormat, codec, ref bytesCounter, ref fpsCounter);
                }

                // 帧率控制延时
                var elapsedMs = (int)(DateTime.UtcNow - loopStart).TotalMilliseconds;
                int sleepMs = intervalMs - elapsedMs;
                if (sleepMs > 0)
                {
                    Thread.Sleep(sleepMs);
                }

                // 统计计算 (每秒计算一次 FPS 与码率)
                var now = DateTime.UtcNow;
                var statElapsed = (now - lastStatTime).TotalSeconds;
                if (statElapsed >= 1.0)
                {
                    CurrentFps = (int)(fpsCounter / statElapsed);
                    CurrentBitrateKbps = (bytesCounter / 1024.0) / statElapsed;
                    fpsCounter = 0;
                    bytesCounter = 0;
                    lastStatTime = now;
                }
            }
        }

        private void TryCaptureAndSendFrame(int maxFps, int crf, int quality, PixelFormatMode pixelFormat, VideoCodecType codec, ref long bytesCounter, ref int fpsCounter)
        {
            if (!_capturer.Capture(out byte[] bgraBuffer, out int width, out int height, out int stride, out List<Rectangle> dirtyRects, out bool isKeyFrame))
            {
                return;
            }

            // 防网络积压机制：如果前一次推包仍在排队，则主动丢弃本帧但累加脏矩形范围
            if (!Monitor.TryEnter(_pushLock))
            {
                lock (_accumulatedDirtyRects)
                {
                    if (isKeyFrame || _forceKeyFrame)
                    {
                        _accumulatedDirtyRects.Clear();
                        _accumulatedDirtyRects.Add(new Rectangle(0, 0, width, height));
                    }
                    else
                    {
                        _accumulatedDirtyRects.AddRange(dirtyRects);
                    }
                }
                return;
            }

            try
            {
                bool forceKey = isKeyFrame || _forceKeyFrame;

                // 1. FFmpeg H.264 视频流编码分支（默认主通道）
                if (codec == VideoCodecType.H264)
                {
                    // 若画面完全无变动且未强制请求关键帧，则跳过传输以省带宽和 CPU
                    if (!forceKey && dirtyRects.Count == 0 && _accumulatedDirtyRects.Count == 0)
                    {
                        return;
                    }

                    lock (_syncLock)
                    {
                        if (_h264Encoder == null || _h264Encoder.Width != width || _h264Encoder.Height != height)
                        {
                            _h264Encoder?.Dispose();
                            _h264Encoder = new FFmpegH264Encoder(width, height, maxFps, crf);
                            forceKey = true;
                        }
                    }

                    byte[]? videoPacket = _h264Encoder.EncodeFrame(bgraBuffer, width, height, stride, forceKey, out bool outIsKeyFrame);
                    if (videoPacket != null && videoPacket.Length > 0)
                    {
                        _forceKeyFrame = false;

                        var packet = new ScreenFramePacketDto
                        {
                            FrameIndex = Interlocked.Increment(ref _frameIndex),
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            DisplayWidth = width,
                            DisplayHeight = height,
                            IsKeyFrame = outIsKeyFrame,
                            Codec = VideoCodecType.H264,
                            VideoData = videoPacket
                        };

                        bytesCounter += videoPacket.Length;
                        fpsCounter++;
                        FrameArrived?.Invoke(this, packet);

                        if (Interlocked.Decrement(ref _remainingRefreshQuota) < 0)
                        {
                            Interlocked.Exchange(ref _remainingRefreshQuota, 0);
                        }
                    }
                    return;
                }

                // 2. TurboJPEG 兼容模式：切片脏矩形编码分支
                List<Rectangle> finalRects;
                lock (_accumulatedDirtyRects)
                {
                    if (_accumulatedDirtyRects.Count > 0)
                    {
                        _accumulatedDirtyRects.AddRange(dirtyRects);
                        finalRects = new List<Rectangle>(_accumulatedDirtyRects);
                        _accumulatedDirtyRects.Clear();
                    }
                    else
                    {
                        finalRects = dirtyRects;
                    }
                }

                var optimizedRects = forceKey
                    ? new List<Rectangle> { new Rectangle(0, 0, width, height) }
                    : DirtyRectOptimizer.Optimize(finalRects);

                if (optimizedRects.Count == 0) return;

                var jpegPacket = new ScreenFramePacketDto
                {
                    FrameIndex = Interlocked.Increment(ref _frameIndex),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DisplayWidth = width,
                    DisplayHeight = height,
                    IsKeyFrame = forceKey,
                    Codec = VideoCodecType.Jpeg
                };

                long frameBytes = 0;
                int bytesPerPixel = (pixelFormat == PixelFormatMode.Bgra32) ? 4 : 3;

                foreach (var rect in optimizedRects)
                {
                    var clamped = DirtyRectOptimizer.Clamp(rect, width, height);
                    if (clamped.Width <= 0 || clamped.Height <= 0) continue;

                    int subStride = clamped.Width * bytesPerPixel;
                    int requiredBytes = subStride * clamped.Height;

                    byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(requiredBytes);
                    try
                    {
                        ExtractSubImage(bgraBuffer, width, height, stride, clamped, rentedBuffer, subStride, pixelFormat);
                        byte[] compressed = TurboJpegCodec.Compress(rentedBuffer, clamped.Width, clamped.Height, subStride, pixelFormat, quality);

                        if (compressed.Length > 0)
                        {
                            jpegPacket.Blocks.Add(new DirtyBlockDto
                            {
                                X = clamped.X,
                                Y = clamped.Y,
                                Width = clamped.Width,
                                Height = clamped.Height,
                                PixelFormat = pixelFormat,
                                CompressedData = compressed
                            });
                            frameBytes += compressed.Length;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }

                if (jpegPacket.Blocks.Count > 0)
                {
                    _forceKeyFrame = false;
                    bytesCounter += frameBytes;
                    fpsCounter++;
                    FrameArrived?.Invoke(this, jpegPacket);

                    if (Interlocked.Decrement(ref _remainingRefreshQuota) < 0)
                    {
                        Interlocked.Exchange(ref _remainingRefreshQuota, 0);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_pushLock);
            }
        }

        private static void ExtractSubImage(byte[] source, int fullWidth, int fullHeight, int fullStride, Rectangle rect, byte[] dest, int destStride, PixelFormatMode formatMode)
        {
            int rectW = rect.Width;
            int rectH = rect.Height;

            if (formatMode == PixelFormatMode.Bgra32)
            {
                int copyBytes = rectW * 4;
                for (int row = 0; row < rectH; row++)
                {
                    int srcOffset = (rect.Y + row) * fullStride + (rect.X * 4);
                    int dstOffset = row * destStride;
                    Buffer.BlockCopy(source, srcOffset, dest, dstOffset, copyBytes);
                }
            }
            else if (formatMode == PixelFormatMode.Rgb565)
            {
                for (int row = 0; row < rectH; row++)
                {
                    int srcOffset = (rect.Y + row) * fullStride + (rect.X * 4);
                    int dstOffset = row * destStride;

                    for (int col = 0; col < rectW; col++)
                    {
                        int sIdx = srcOffset + (col * 4);
                        int dIdx = dstOffset + (col * 3);

                        byte b = source[sIdx];
                        byte g = source[sIdx + 1];
                        byte r = source[sIdx + 2];

                        dest[dIdx] = (byte)(b & 0xF8);
                        dest[dIdx + 1] = (byte)(g & 0xFC);
                        dest[dIdx + 2] = (byte)(r & 0xF8);
                    }
                }
            }
            else // Rgb24
            {
                for (int row = 0; row < rectH; row++)
                {
                    int srcOffset = (rect.Y + row) * fullStride + (rect.X * 4);
                    int dstOffset = row * destStride;

                    for (int col = 0; col < rectW; col++)
                    {
                        int sIdx = srcOffset + (col * 4);
                        int dIdx = dstOffset + (col * 3);

                        dest[dIdx] = source[sIdx];
                        dest[dIdx + 1] = source[sIdx + 1];
                        dest[dIdx + 2] = source[sIdx + 2];
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _quotaWakeEvent.Dispose();
            _h264Encoder?.Dispose();
            _capturer?.Dispose();
        }
    }
}
