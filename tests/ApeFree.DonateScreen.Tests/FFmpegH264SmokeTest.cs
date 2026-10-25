using ApeFree.DonateScreen.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class FFmpegH264SmokeTest
    {
        [TestMethod]
        public void TestH264EncodeAndDecodeRoundtrip()
        {
            // 确保 FFmpeg 运行时库加载成功
            FFmpegInitializer.EnsureInitialized();

            int width = 640;
            int height = 360;
            int stride = width * 4;

            // 构造测试色彩画面 (带渐变测试数据)
            byte[] rawBgra = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * 4;
                    rawBgra[offset] = (byte)(x % 256);         // B
                    rawBgra[offset + 1] = (byte)(y % 256);     // G
                    rawBgra[offset + 2] = (byte)((x + y) % 256);// R
                    rawBgra[offset + 3] = 255;                 // A
                }
            }

            // 1. 初始化编码器与解码器
            using var encoder = new FFmpegH264Encoder(width, height, fps: 30, crf: 25);
            using var decoder = new FFmpegH264Decoder();

            // 2. 编码关键帧 (IDR)
            byte[]? keyPacket = encoder.EncodeFrame(rawBgra, width, height, stride, forceKeyFrame: true, out bool isKeyFrame);
            Assert.IsNotNull(keyPacket, "编码关键帧失败，返回 null");
            Assert.IsTrue(keyPacket.Length > 0, "关键帧数据长度应大于 0");
            Assert.IsTrue(isKeyFrame, "强制关键帧标志应为 true");

            // 3. 解码关键帧到目标 BGRA 缓冲区
            byte[] decodedBgra = new byte[height * stride];
            bool decodeSuccess = decoder.DecodePacket(keyPacket, decodedBgra, width, height, stride, out int outW, out int outH);
            Assert.IsTrue(decodeSuccess, "解码关键帧失败");
            Assert.AreEqual(width, outW);
            Assert.AreEqual(height, outH);

            // 验证解码后的非空像素 (允许有损压缩色彩微差)
            long nonZeroBytes = 0;
            for (int i = 0; i < decodedBgra.Length; i++)
            {
                if (decodedBgra[i] > 0) nonZeroBytes++;
            }
            Assert.IsTrue(nonZeroBytes > decodedBgra.Length / 2, "解码画面应包含大量有效色彩数据");

            // 4. 连续编码第 2 帧 (P 帧)
            byte[]? pPacket = encoder.EncodeFrame(rawBgra, width, height, stride, forceKeyFrame: false, out bool isPKeyFrame);
            Assert.IsNotNull(pPacket, "编码 P 帧失败，返回 null");
            Assert.IsFalse(isPKeyFrame, "非强制关键帧应为 P 帧");

            // 解码第 2 帧
            bool decodeP = decoder.DecodePacket(pPacket, decodedBgra, width, height, stride, out outW, out outH);
            Assert.IsTrue(decodeP, "解码 P 帧失败");
        }
    }
}
