using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ApeFree.DonateScreen.Codecs
{
    /// <summary>
    /// 基于 FFmpeg libavcodec 的实时 H.264 视频流解码器。
    /// 支持直接将 H.264 NALU / 视频数据包解码并零拷贝转换至 WPF WriteableBitmap 的 BackBuffer 显存中。
    /// </summary>
    public unsafe class FFmpegH264Decoder : IDisposable
    {
        private readonly object _syncLock = new();
        private AVCodecContext* _decCtx;
        private SwsContext* _swsCtx;
        private AVFrame* _decodedFrame;
        private AVPacket* _packet;

        private int _currentSrcWidth;
        private int _currentSrcHeight;
        private AVPixelFormat _currentSrcFormat;
        private int _currentTargetWidth;
        private int _currentTargetHeight;
        private bool _isDisposed;

        public FFmpegH264Decoder()
        {
            FFmpegInitializer.EnsureInitialized();
            InitDecoder();
        }

        private void InitDecoder()
        {
            var decoder = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (decoder == null)
            {
                throw new InvalidOperationException("未找到可用的 H.264 解码器，请确保 FFmpeg 运行库已正确部署。");
            }

            _decCtx = ffmpeg.avcodec_alloc_context3(decoder);
            if (_decCtx == null)
            {
                throw new OutOfMemoryException("无法分配解码器 AVCodecContext。");
            }

            _decCtx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _decCtx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            int ret = ffmpeg.avcodec_open2(_decCtx, decoder, null);
            if (ret < 0)
            {
                throw new InvalidOperationException($"打开 H.264 解码器失败，错误码: {ret}");
            }

            _decodedFrame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
        }

        /// <summary>
        /// 解码 H.264 数据包并将画面直接转换为 BGRA32 写入目标内存指针 (如 WriteableBitmap.BackBuffer)
        /// </summary>
        public bool DecodePacket(byte[] videoData, IntPtr dstBuffer, int targetWidth, int targetHeight, int targetStride, out int decodedWidth, out int decodedHeight)
        {
            decodedWidth = 0;
            decodedHeight = 0;

            if (videoData == null || videoData.Length == 0 || dstBuffer == IntPtr.Zero || targetWidth <= 0 || targetHeight <= 0)
            {
                return false;
            }

            lock (_syncLock)
            {
                if (_isDisposed || _decCtx == null) return false;

                fixed (byte* pData = videoData)
                {
                    _packet->data = pData;
                    _packet->size = videoData.Length;

                    int sendRet = ffmpeg.avcodec_send_packet(_decCtx, _packet);
                    _packet->data = null;
                    _packet->size = 0;

                    if (sendRet < 0)
                    {
                        return false;
                    }

                    int recRet = ffmpeg.avcodec_receive_frame(_decCtx, _decodedFrame);
                    if (recRet == 0)
                    {
                        decodedWidth = _decodedFrame->width;
                        decodedHeight = _decodedFrame->height;
                        var srcFmt = (AVPixelFormat)_decodedFrame->format;

                        // 确保颜色转换器与分辨率、色彩格式匹配
                        EnsureSwsContext(decodedWidth, decodedHeight, srcFmt, targetWidth, targetHeight);

                        if (_swsCtx != null)
                        {
                            byte_ptrArray4 dstData = new byte_ptrArray4();
                            dstData[0] = (byte*)dstBuffer;
                            int_array4 dstLinesize = new int_array4();
                            dstLinesize[0] = targetStride > 0 ? targetStride : targetWidth * 4;

                            ffmpeg.sws_scale(
                                _swsCtx,
                                _decodedFrame->data,
                                _decodedFrame->linesize,
                                0,
                                decodedHeight,
                                dstData,
                                dstLinesize
                            );

                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 解码 H.264 数据包并输出至托管字节数组
        /// </summary>
        public bool DecodePacket(byte[] videoData, byte[] outBgraBuffer, int targetWidth, int targetHeight, int targetStride, out int decodedWidth, out int decodedHeight)
        {
            if (outBgraBuffer == null || outBgraBuffer.Length == 0)
            {
                decodedWidth = 0;
                decodedHeight = 0;
                return false;
            }

            fixed (byte* pOut = outBgraBuffer)
            {
                return DecodePacket(videoData, (IntPtr)pOut, targetWidth, targetHeight, targetStride, out decodedWidth, out decodedHeight);
            }
        }

        private void EnsureSwsContext(int srcW, int srcH, AVPixelFormat srcFmt, int dstW, int dstH)
        {
            if (_swsCtx != null &&
                _currentSrcWidth == srcW &&
                _currentSrcHeight == srcH &&
                _currentSrcFormat == srcFmt &&
                _currentTargetWidth == dstW &&
                _currentTargetHeight == dstH)
            {
                return;
            }

            if (_swsCtx != null)
            {
                ffmpeg.sws_freeContext(_swsCtx);
                _swsCtx = null;
            }

            _currentSrcWidth = srcW;
            _currentSrcHeight = srcH;
            _currentSrcFormat = srcFmt;
            _currentTargetWidth = dstW;
            _currentTargetHeight = dstH;

            _swsCtx = ffmpeg.sws_getContext(
                srcW,
                srcH,
                srcFmt,
                dstW,
                dstH,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                2, // SWS_BILINEAR
                null,
                null,
                null
            );
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

            if (_decodedFrame != null)
            {
                fixed (AVFrame** ppFrame = &_decodedFrame)
                {
                    ffmpeg.av_frame_free(ppFrame);
                }
                _decodedFrame = null;
            }

            if (_decCtx != null)
            {
                fixed (AVCodecContext** ppCtx = &_decCtx)
                {
                    ffmpeg.avcodec_free_context(ppCtx);
                }
                _decCtx = null;
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

        ~FFmpegH264Decoder()
        {
            Dispose();
        }
    }
}
