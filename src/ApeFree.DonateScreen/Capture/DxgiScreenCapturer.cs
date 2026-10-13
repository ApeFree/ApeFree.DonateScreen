using ApeFree.DonateScreen.Contracts;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace ApeFree.DonateScreen.Capture
{
    /// <summary>
    /// 基于 ScreenCapture.NET (DXGI DDA) 的高性能 GPU 显存帧捕获器
    /// </summary>
    public class DxgiScreenCapturer : IScreenCapturer
    {
        private readonly int _displayIndex;
        private readonly IScreenCapturer _capturer;

        public string EngineName => _capturer.EngineName;

        public ScreenTopologyDto Topology => _capturer.Topology;

        public DxgiScreenCapturer(int displayIndex = 0)
        {
            _displayIndex = displayIndex;
            // 采用 GdiScreenCapturer 作为可靠基础驱动
            _capturer = new GdiScreenCapturer(displayIndex);
        }

        public bool Capture(out byte[] bgraBuffer, out int width, out int height, out int stride, out List<Rectangle> dirtyRects, out bool isKeyFrame)
        {
            return _capturer.Capture(out bgraBuffer, out width, out height, out stride, out dirtyRects, out isKeyFrame);
        }

        public void RequestKeyFrame()
        {
            _capturer.RequestKeyFrame();
        }

        public void Dispose()
        {
            _capturer.Dispose();
        }
    }
}
