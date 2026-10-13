using ApeFree.DonateScreen.Contracts;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace ApeFree.DonateScreen.Capture
{
    /// <summary>
    /// 屏幕捕获器抽象接口
    /// </summary>
    public interface IScreenCapturer : IDisposable
    {
        /// <summary>
        /// 捕获器的名称或驱动类型
        /// </summary>
        string EngineName { get; }

        /// <summary>
        /// 当前屏幕拓扑与分辨率信息
        /// </summary>
        ScreenTopologyDto Topology { get; }

        /// <summary>
        /// 捕获一帧图像
        /// </summary>
        /// <param name="bgraBuffer">整帧 BGRA32 像素数据缓冲区</param>
        /// <param name="width">画面宽度</param>
        /// <param name="height">画面高度</param>
        /// <param name="stride">行扫描步长</param>
        /// <param name="dirtyRects">发生变化的脏矩形区域列表</param>
        /// <param name="isKeyFrame">是否为全量关键帧</param>
        /// <returns>是否有画面产出（若画面完全静止且未请求关键帧则返回 false）</returns>
        bool Capture(out byte[] bgraBuffer, out int width, out int height, out int stride, out List<Rectangle> dirtyRects, out bool isKeyFrame);

        /// <summary>
        /// 请求下一帧必须产出全屏关键帧
        /// </summary>
        void RequestKeyFrame();
    }
}
