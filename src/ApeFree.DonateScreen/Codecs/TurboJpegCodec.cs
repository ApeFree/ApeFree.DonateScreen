using ApeFree.DonateScreen.Contracts;
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

        [ThreadStatic]
        private static TJCompressor? t_compressor;

        [ThreadStatic]
        private static TJDecompressor? t_decompressor;

        static TurboJpegCodec()
        {
            try
            {
                // 探测加载 turbojpeg.dll（按 x64/x86 架构路径优先定位）
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string archDir = Environment.Is64BitProcess ? "x64" : "x86";
                string archDllPath = Path.Combine(baseDir, archDir, "turbojpeg.dll");

                IntPtr hModule = IntPtr.Zero;
                if (File.Exists(archDllPath))
                {
                    hModule = LoadLibrary(archDllPath);
                }

                if (hModule == IntPtr.Zero)
                {
                    string rootDllPath = Path.Combine(baseDir, "turbojpeg.dll");
                    if (File.Exists(rootDllPath))
                    {
                        hModule = LoadLibrary(rootDllPath);
                    }
                }

                if (hModule == IntPtr.Zero)
                {
                    hModule = LoadLibrary("turbojpeg.dll");
                }

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

        private static TJCompressor GetCompressor()
        {
            return t_compressor ??= new TJCompressor();
        }

        private static TJDecompressor GetDecompressor()
        {
            return t_decompressor ??= new TJDecompressor();
        }

        /// <summary>
        /// 压缩指定格式像素数据为 JPEG
        /// </summary>
        public static byte[] Compress(byte[] pixelData, int width, int height, int stride = 0, PixelFormatMode formatMode = PixelFormatMode.Rgb24, int quality = 75)
        {
            if (width <= 0 || height <= 0 || pixelData == null || pixelData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            int bytesPerPixel = (formatMode == PixelFormatMode.Bgra32) ? 4 : 3;
            if (stride <= 0)
            {
                stride = width * bytesPerPixel;
            }

            if (_useTurboJpeg)
            {
                try
                {
                    var tjFormat = (formatMode == PixelFormatMode.Bgra32)
                        ? TJPixelFormats.TJPF_BGRA
                        : TJPixelFormats.TJPF_BGR;

                    var compressor = GetCompressor();
                    return compressor.Compress(
                        pixelData,
                        stride,
                        width,
                        height,
                        tjFormat,
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

            return FallbackCompress(pixelData, width, height, stride, formatMode, quality);
        }

        /// <summary>
        /// 压缩 BGRA32 字节数组为 JPEG 字节数组
        /// </summary>
        public static byte[] CompressBgra(byte[] bgraData, int width, int height, int stride = 0, int quality = 75)
        {
            return Compress(bgraData, width, height, stride, PixelFormatMode.Bgra32, quality);
        }

        /// <summary>
        /// 从内存指针压缩 BGRA32 图像数据
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
                    var decompressor = GetDecompressor();
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
        private static byte[] FallbackCompress(byte[] pixelData, int width, int height, int stride, PixelFormatMode formatMode, int quality)
        {
            var pixelFormat = (formatMode == PixelFormatMode.Bgra32)
                ? System.Windows.Media.PixelFormats.Bgra32
                : System.Windows.Media.PixelFormats.Bgr24;

            var bitmapSource = BitmapSource.Create(
                width,
                height,
                96,
                96,
                pixelFormat,
                null,
                pixelData,
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
