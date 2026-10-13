using ApeFree.DonateScreen.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class TurboJpegSmokeTest
    {
        [TestMethod]
        public void TestTurboJpegCompressAndDecompress()
        {
            // 构造 64x64 的 BGRA 测试数据
            int width = 64;
            int height = 64;
            byte[] rawBgra = new byte[width * height * 4];
            for (int i = 0; i < rawBgra.Length; i += 4)
            {
                rawBgra[i] = 255;     // B
                rawBgra[i + 1] = 128; // G
                rawBgra[i + 2] = 0;   // R
                rawBgra[i + 3] = 255; // A
            }

            // 使用项目的统一编解码器 TurboJpegCodec 进行压缩
            byte[] jpegData = TurboJpegCodec.CompressBgra(rawBgra, width, height, width * 4, 80);
            Assert.IsNotNull(jpegData);
            Assert.IsTrue(jpegData.Length > 0);

            // 进行解压
            byte[] decompressed = TurboJpegCodec.DecompressBgra(jpegData, out int outWidth, out int outHeight, out int outStride);
            Assert.AreEqual(width, outWidth);
            Assert.AreEqual(height, outHeight);
            Assert.IsNotNull(decompressed);
            Assert.AreEqual(width * height * 4, decompressed.Length);
        }
    }
}
