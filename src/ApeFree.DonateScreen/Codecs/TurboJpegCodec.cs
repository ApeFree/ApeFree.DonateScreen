using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using TurboJpegWrapper;

namespace ApeFree.DonateScreen.Codecs
{
    /// <summary>
    /// 高性能 TurboJPEG 编解码器（支持 SIMD 硬件加速与纯托管安全降级）
    /// </summary>
    public static class TurboJpegCodec
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private static bool _useTurboJpeg = false;

        static TurboJpegCodec()
        {
            try
            {
                // 预先探测系统/运行目录下是否存在可加载的 turbojpeg.dll
                var hModule = LoadLibrary("turbojpeg.dll");
                if (hModule != IntPtr.Zero)
                {
                    _useTurboJpeg = true;
                }
            }
            catch
            {
                _useTurboJpeg = false;
            }
        }

        /// <summary>
        /// 是否优先使用 TurboJPEG
        /// </summary>
        public static bool UseTurboJpeg
        {
            get => _useTurboJpeg;
            set => _useTurboJpeg = value;
        }

        /// <summary>
        /// 压缩 BGRA32 字节数组为 JPEG 字节数组
        /// </summary>
        public static byte[] CompressBgra(byte[] bgraData, int width, int height, int stride = 0, int quality = 75)
        {
            if (width <= 0 || height <= 0 || bgraData == null || bgraData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            if (stride <= 0)
            {
                stride = width * 4;
            }

            if (_useTurboJpeg)
            {
                try
                {
                    using var compressor = new TJCompressor();
                    return compressor.Compress(
                        bgraData,
                        width,
                        stride,
                        height,
                        TJPixelFormats.TJPF_BGRA,
                        TJSubsamplingOptions.TJSAMP_420,
                        quality,
                        TJFlags.NONE
                    );
                }
                catch
                {
                    _useTurboJpeg = false;
                }
            }

            return FallbackCompress(bgraData, width, height, stride, quality);
        }

        /// <summary>
        /// 从内存指针压缩 BGRA32 图像数据（零拷贝）
        /// </summary>
        public static unsafe byte[] CompressBgra(IntPtr pBgra, int width, int height, int stride = 0, int quality = 75)
        {
            if (pBgra == IntPtr.Zero || width <= 0 || height <= 0)
            {
                return Array.Empty<byte>();
            }

            if (stride <= 0)
            {
                stride = width * 4;
            }

            int byteCount = stride * height;
            byte[] managedBuffer = new byte[byteCount];
            Marshal.Copy(pBgra, managedBuffer, 0, byteCount);

            return CompressBgra(managedBuffer, width, height, stride, quality);
        }

        /// <summary>
        /// 解码 JPEG 字节数组为 BGRA32 原始数据
        /// </summary>
        public static byte[] DecompressBgra(byte[] jpegData, out int width, out int height, out int stride)
        {
            width = 0;
            height = 0;
            stride = 0;

            if (jpegData == null || jpegData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            if (_useTurboJpeg)
            {
                try
                {
                    using var decompressor = new TJDecompressor();
                    byte[] decompressed = decompressor.Decompress(
                        jpegData,
                        TJPixelFormats.TJPF_BGRA,
                        TJFlags.NONE,
                        out width,
                        out height,
                        out stride
                    );
                    return decompressed;
                }
                catch
                {
                    _useTurboJpeg = false;
                }
            }

            return FallbackDecompress(jpegData, out width, out height, out stride);
        }

        /// <summary>
        /// 托管降级压缩实现
        /// </summary>
        private static byte[] FallbackCompress(byte[] bgraData, int width, int height, int stride, int quality)
        {
            var bitmapSource = BitmapSource.Create(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                bgraData,
                stride
            );

            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// 托管降级解压实现
        /// </summary>
        private static byte[] FallbackDecompress(byte[] jpegData, out int width, out int height, out int stride)
        {
            using var ms = new MemoryStream(jpegData);
            var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            width = frame.PixelWidth;
            height = frame.PixelHeight;
            stride = width * 4;

            var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            byte[] pixelData = new byte[stride * height];
            converted.CopyPixels(pixelData, stride, 0);

            return pixelData;
        }
    }
}
