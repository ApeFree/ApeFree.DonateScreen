using ApeFree.DonateScreen.Codecs;
using ApeFree.DonateScreen.Contracts;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApeFree.DonateScreen.Rendering
{
    /// <summary>
    /// 高性能 WPF WriteableBitmap 局部增量渲染与硬件位块传送器 (Blitter)
    /// </summary>
    public class WriteableBitmapBlitter
    {
        private WriteableBitmap? _bitmap;
        private int _currentWidth;
        private int _currentHeight;
        private readonly object _renderLock = new();

        /// <summary>
        /// 当前绑定的可写位图（可直接绑定至 WPF Image.Source）
        /// </summary>
        public WriteableBitmap? Bitmap => _bitmap;

        /// <summary>
        /// 画面尺寸发生重置或变更事件
        /// </summary>
        public event EventHandler<WriteableBitmap>? BitmapReset;

        /// <summary>
        /// 初始化或调整位图尺寸
        /// </summary>
        public void EnsureBitmapSize(int width, int height)
        {
            if (_bitmap != null && _currentWidth == width && _currentHeight == height)
            {
                return;
            }

            lock (_renderLock)
            {
                _currentWidth = width;
                _currentHeight = height;
                _bitmap = new WriteableBitmap(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null
                );

                BitmapReset?.Invoke(this, _bitmap);
            }
        }

        /// <summary>
        /// 渲染接收到的增量帧数据包（必须在 UI 线程或 Dispatcher 中调用）
        /// </summary>
        public void RenderPacket(ScreenFramePacketDto packet)
        {
            if (packet == null || packet.Blocks == null || packet.Blocks.Count == 0)
            {
                return;
            }

            // 检查分辨率是否匹配
            if (_bitmap == null || _currentWidth != packet.DisplayWidth || _currentHeight != packet.DisplayHeight)
            {
                EnsureBitmapSize(packet.DisplayWidth, packet.DisplayHeight);
            }

            var bmp = _bitmap;
            if (bmp == null) return;

            bmp.Lock();
            try
            {
                IntPtr backBuffer = bmp.BackBuffer;
                int bmpStride = bmp.BackBufferStride;

                foreach (var block in packet.Blocks)
                {
                    if (block.CompressedData == null || block.CompressedData.Length == 0) continue;

                    // 使用 TurboJPEG SIMD 解压局部脏矩形
                    byte[] decompressed = TurboJpegCodec.DecompressBgra(block.CompressedData, out int blockW, out int blockH, out int blockStride);
                    if (decompressed.Length == 0 || blockW <= 0 || blockH <= 0) continue;

                    // 计算向 BackBuffer 拷贝的矩形区域
                    int copyW = Math.Min(blockW, _currentWidth - block.X);
                    int copyH = Math.Min(blockH, _currentHeight - block.Y);
                    if (copyW <= 0 || copyH <= 0) continue;

                    int bytesPerRow = copyW * 4;

                    unsafe
                    {
                        byte* pDstBase = (byte*)backBuffer;
                        fixed (byte* pSrcBase = decompressed)
                        {
                            for (int row = 0; row < copyH; row++)
                            {
                                byte* pSrcRow = pSrcBase + (row * blockStride);
                                byte* pDstRow = pDstBase + ((block.Y + row) * bmpStride) + (block.X * 4);
                                Buffer.MemoryCopy(pSrcRow, pDstRow, bytesPerRow, bytesPerRow);
                            }
                        }
                    }

                    // 标记 GPU 硬件增量重绘区域
                    bmp.AddDirtyRect(new Int32Rect(block.X, block.Y, copyW, copyH));
                }
            }
            finally
            {
                bmp.Unlock();
            }
        }
    }
}
