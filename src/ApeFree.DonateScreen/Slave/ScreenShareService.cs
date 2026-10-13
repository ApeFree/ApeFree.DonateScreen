using ApeFree.DonateScreen.Capture;
using ApeFree.DonateScreen.Codecs;
using ApeFree.DonateScreen.Contracts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// 远程屏幕共享服务实现（由 Slave 节点暴露）
    /// 负责启动屏幕抓取循环、脏矩形提取、TurboJPEG 压缩与事件推流。
    /// </summary>
    public class ScreenShareService : IScreenShareService, IDisposable
    {
        private readonly object _syncLock = new();
        private readonly object _pushLock = new();
        private IScreenCapturer _capturer;
        private Thread? _workerThread;
        private volatile bool _isRunning;
        private volatile bool _isPaused;
        private long _frameIndex;
        private CaptureOptionsDto _options = new();
        private readonly List<Rectangle> _accumulatedDirtyRects = new();

        public event EventHandler<ScreenFramePacketDto>? FrameArrived;
        public event EventHandler<ScreenTopologyDto>? TopologyChanged;

        public ScreenTopologyDto ScreenTopology => _capturer.Topology;

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
        public string EngineName => _capturer.EngineName;

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
            _capturer.RequestKeyFrame();
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
            }
        }

        public void SetStreamingPaused(bool isPaused)
        {
            _isPaused = isPaused;
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
                int maxFps;
                int quality;
                lock (_syncLock)
                {
                    maxFps = Math.Clamp(_options.MaxFps, 5, 60);
                    quality = Math.Clamp(_options.JpegQuality, 10, 95);
                }

                int intervalMs = Math.Max(1, 1000 / maxFps);
                var loopStart = DateTime.UtcNow;

                if (!_isPaused && FrameArrived != null)
                {
                    TryCaptureAndSendFrame(quality, ref bytesCounter, ref fpsCounter);
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

        private void TryCaptureAndSendFrame(int quality, ref long bytesCounter, ref int fpsCounter)
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
                    if (isKeyFrame)
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

                // 优化合并细碎脏矩形
                var optimizedRects = isKeyFrame
                    ? new List<Rectangle> { new Rectangle(0, 0, width, height) }
                    : DirtyRectOptimizer.Optimize(finalRects);

                if (optimizedRects.Count == 0) return;

                var packet = new ScreenFramePacketDto
                {
                    FrameIndex = Interlocked.Increment(ref _frameIndex),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DisplayWidth = width,
                    DisplayHeight = height,
                    IsKeyFrame = isKeyFrame
                };

                long frameBytes = 0;

                foreach (var rect in optimizedRects)
                {
                    var clamped = DirtyRectOptimizer.Clamp(rect, width, height);
                    if (clamped.Width <= 0 || clamped.Height <= 0) continue;

                    byte[] subBgra = ExtractSubImage(bgraBuffer, width, height, stride, clamped);
                    byte[] compressed = TurboJpegCodec.CompressBgra(subBgra, clamped.Width, clamped.Height, clamped.Width * 4, quality);

                    if (compressed.Length > 0)
                    {
                        packet.Blocks.Add(new DirtyBlockDto
                        {
                            X = clamped.X,
                            Y = clamped.Y,
                            Width = clamped.Width,
                            Height = clamped.Height,
                            CompressedData = compressed
                        });
                        frameBytes += compressed.Length;
                    }
                }

                if (packet.Blocks.Count > 0)
                {
                    bytesCounter += frameBytes;
                    fpsCounter++;
                    FrameArrived?.Invoke(this, packet);
                }
            }
            finally
            {
                Monitor.Exit(_pushLock);
            }
        }

        /// <summary>
        /// 从全屏 BGRA 缓冲区裁剪出子矩形区域像素
        /// </summary>
        private static byte[] ExtractSubImage(byte[] source, int fullWidth, int fullHeight, int fullStride, Rectangle rect)
        {
            int subStride = rect.Width * 4;
            byte[] sub = new byte[subStride * rect.Height];

            int srcBytesPerPixel = 4;
            int copyBytes = rect.Width * srcBytesPerPixel;

            for (int row = 0; row < rect.Height; row++)
            {
                int srcOffset = (rect.Y + row) * fullStride + rect.X * srcBytesPerPixel;
                int dstOffset = row * subStride;

                if (srcOffset + copyBytes <= source.Length && dstOffset + copyBytes <= sub.Length)
                {
                    Buffer.BlockCopy(source, srcOffset, sub, dstOffset, copyBytes);
                }
            }

            return sub;
        }

        public void Dispose()
        {
            Stop();
            _capturer?.Dispose();
        }
    }
}
