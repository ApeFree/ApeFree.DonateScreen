using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ApeFree.DonateScreen.Codecs
{
    /// <summary>
    /// 基于 FFmpeg libavcodec (libx264) 的实时超低延迟 H.264 视频流编码器。
    /// 针对远程桌面场景深度调优：ultrafast 预设、zerolatency 无 B 帧模式，毫秒级即时出帧。
    /// </summary>
    public unsafe class FFmpegH264Encoder : IDisposable
    {
        private readonly object _syncLock = new();
        private AVCodecContext* _encCtx;
        private SwsContext* _swsCtx;
        private AVFrame* _yuvFrame;
        private AVPacket* _packet;

        private int _currentWidth;
        private int _currentHeight;
        private int _fps;
        private int _crf;
        private long _ptsIndex;
        private bool _isDisposed;

        /// <summary>
        /// 当前编码宽度
        /// </summary>
        public int Width => _currentWidth;

        /// <summary>
        /// 当前编码高度
        /// </summary>
        public int Height => _currentHeight;

        /// <summary>
        /// 目标帧率
        /// </summary>
        public int Fps => _fps;

        public FFmpegH264Encoder(int width, int height, int fps = 30, int crf = 25)
        {
            FFmpegInitializer.EnsureInitialized();
            _fps = Math.Clamp(fps, 5, 60);
            _crf = Math.Clamp(crf, 15, 40);
            EnsureEncoder(width, height);
        }

        /// <summary>
        /// 调整编码配置
        /// </summary>
        public void UpdateConfig(int fps, int crf)
        {
            lock (_syncLock)
            {
                _fps = Math.Clamp(fps, 5, 60);
                _crf = Math.Clamp(crf, 15, 40);
            }
        }

        private void EnsureEncoder(int width, int height)
        {
            if (_encCtx != null && _currentWidth == width && _currentHeight == height)
            {
                return;
            }

            ReleaseNativeResources();

            _currentWidth = width;
            _currentHeight = height;

            var encoder = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (encoder == null)
            {
                encoder = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            }

            if (encoder == null)
            {
                throw new InvalidOperationException("未找到可用的 H.264 / libx264 编码器，请确保 FFmpeg 运行库已正确部署。");
            }

            _encCtx = ffmpeg.avcodec_alloc_context3(encoder);
            if (_encCtx == null)
            {
                throw new OutOfMemoryException("无法分配 AVCodecContext。");
            }

            _encCtx->width = width;
            _encCtx->height = height;
            _encCtx->time_base = new AVRational { num = 1, den = _fps };
            _encCtx->framerate = new AVRational { num = _fps, den = 1 };
            _encCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _encCtx->gop_size = _fps * 4; // 默认 4 秒一个周期 I 帧，中间全为 P 帧
            _encCtx->max_b_frames = 0;    // 禁用 B 帧，确保输入帧与输出包 1:1 零排队延迟
            _encCtx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

            AVDictionary* opts = null;
            ffmpeg.av_dict_set(&opts, "preset", "ultrafast", 0);
            ffmpeg.av_dict_set(&opts, "tune", "zerolatency", 0);
            ffmpeg.av_dict_set(&opts, "crf", _crf.ToString(), 0);

            int ret = ffmpeg.avcodec_open2(_encCtx, encoder, &opts);
            ffmpeg.av_dict_free(&opts);

            if (ret < 0)
            {
                throw new InvalidOperationException($"打开 H.264 编码器失败，错误码: {ret}");
            }

            // 分配 YUV420P 帧缓冲区
            _yuvFrame = ffmpeg.av_frame_alloc();
            _yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            _yuvFrame->width = width;
            _yuvFrame->height = height;
            ret = ffmpeg.av_frame_get_buffer(_yuvFrame, 32);
            if (ret < 0)
            {
                throw new InvalidOperationException("为 YUV 图像帧分配底层缓冲区失败。");
            }

            // 创建颜色空间转换上下文 (BGRA32 -> YUV420P)
            _swsCtx = ffmpeg.sws_getContext(
                width,
                height,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                width,
                height,
                AVPixelFormat.AV_PIX_FMT_YUV420P,
                2, // SWS_BILINEAR
                null,
                null,
                null
            );

            if (_swsCtx == null)
            {
                throw new InvalidOperationException("创建 BGRA 到 YUV420P 的 SwsContext 失败。");
            }

            _packet = ffmpeg.av_packet_alloc();
        }

        /// <summary>
        /// 将捕获的 BGRA 图像帧编码为 H.264 视频流数据包
        /// </summary>
        /// <param name="bgraBuffer">全屏 BGRA 图像字节数组</param>
        /// <param name="width">画面宽度</param>
        /// <param name="height">画面高度</param>
        /// <param name="stride">扫描行字节跨度</param>
        /// <param name="forceKeyFrame">是否强制生成全量 IDR 关键帧（如 Master 请求或配额重设）</param>
        /// <param name="isKeyFrame">输出当前帧是否为关键帧</param>
        /// <returns>编码压缩后的 H.264 数据包</returns>
        public byte[]? EncodeFrame(byte[] bgraBuffer, int width, int height, int stride, bool forceKeyFrame, out bool isKeyFrame)
        {
            isKeyFrame = false;
            if (bgraBuffer == null || bgraBuffer.Length == 0 || width <= 0 || height <= 0)
            {
                return null;
            }

            lock (_syncLock)
            {
                if (_isDisposed) return null;

                EnsureEncoder(width, height);

                // 1. 将 BGRA 转换为 YUV420P
                fixed (byte* pBgra = bgraBuffer)
                {
                    byte_ptrArray4 srcData = new byte_ptrArray4();
                    srcData[0] = pBgra;
                    int_array4 srcLinesize = new int_array4();
                    srcLinesize[0] = stride > 0 ? stride : width * 4;

                    ffmpeg.sws_scale(_swsCtx, srcData, srcLinesize, 0, height, _yuvFrame->data, _yuvFrame->linesize);
                }

                _yuvFrame->pts = ++_ptsIndex;

                if (forceKeyFrame)
                {
                    _yuvFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                }
                else
                {
                    _yuvFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
                }

                // 2. 发送帧至编码器
                int ret = ffmpeg.avcodec_send_frame(_encCtx, _yuvFrame);
                if (ret < 0)
                {
                    return null;
                }

                // 3. 从编码器接收压缩报文
                ret = ffmpeg.avcodec_receive_packet(_encCtx, _packet);
                if (ret == 0)
                {
                    try
                    {
                        isKeyFrame = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                        int size = _packet->size;
                        if (size <= 0) return null;

                        byte[] packetBytes = new byte[size];
                        Marshal.Copy((IntPtr)_packet->data, packetBytes, 0, size);
                        return packetBytes;
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(_packet);
                    }
                }

                return null;
            }
        }

        private void ReleaseNativeResources()
        {
            if (_swsCtx != null)
            {
                ffmpeg.sws_freeContext(_swsCtx);
                _swsCtx = null;
            }

            if (_packet != null)
            {
                fixed (AVPacket** ppPkt = &_packet)
                {
                    ffmpeg.av_packet_free(ppPkt);
                }
                _packet = null;
            }

            if (_yuvFrame != null)
            {
                fixed (AVFrame** ppFrame = &_yuvFrame)
                {
                    ffmpeg.av_frame_free(ppFrame);
                }
                _yuvFrame = null;
            }

            if (_encCtx != null)
            {
                fixed (AVCodecContext** ppCtx = &_encCtx)
                {
                    ffmpeg.avcodec_free_context(ppCtx);
                }
                _encCtx = null;
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                ReleaseNativeResources();
            }
            GC.SuppressFinalize(this);
        }

        ~FFmpegH264Encoder()
        {
            Dispose();
        }
    }
}
