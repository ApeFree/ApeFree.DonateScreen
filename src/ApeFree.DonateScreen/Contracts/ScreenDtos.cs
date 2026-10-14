using System;
using System.Collections.Generic;

namespace ApeFree.DonateScreen.Contracts
{
    /// <summary>
    /// 屏幕与控制分享模式
    /// </summary>
    public enum ScreenShareMode
    {
        /// <summary>
        /// 未定义
        /// </summary>
        None = 0,

        /// <summary>
        /// 仅观看屏幕（只读模式：仅暴露屏幕画面服务，不暴露键盘鼠标输入控制）
        /// </summary>
        ViewOnly = 1,

        /// <summary>
        /// 完全控制（同时暴露屏幕画面与键盘鼠标输入控制权限）
        /// </summary>
        FullControl = 2
    }

    /// <summary>
    /// 像素色彩格式选项
    /// </summary>
    public enum PixelFormatMode
    {
        /// <summary>
        /// RGB 24 位真彩色（默认，去除非必要 Alpha 通道，降低 25% 内存与编码开销）
        /// </summary>
        Rgb24 = 0,

        /// <summary>
        /// RGB 16 位高彩色（RGB565，极速低功耗，降低 50% 内存与编码开销）
        /// </summary>
        Rgb565 = 1,

        /// <summary>
        /// BGRA 32 位全彩（保留 4 字节 Alpha 通道）
        /// </summary>
        Bgra32 = 2
    }

    /// <summary>
    /// 鼠标动作标志位
    /// </summary>
    [Flags]
    public enum MouseActionFlags : uint
    {
        None = 0,
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
        Wheel = 0x0800,
        Absolute = 0x8000
    }

    /// <summary>
    /// 单个脏矩形增量图像块 DTO
    /// </summary>
    public class DirtyBlockDto
    {
        /// <summary>
        /// 矩形左上角 X 物理像素坐标
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// 矩形左上角 Y 物理像素坐标
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 矩形宽度（像素）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 矩形高度（像素）
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 像素色彩格式
        /// </summary>
        public PixelFormatMode PixelFormat { get; set; } = PixelFormatMode.Rgb24;

        /// <summary>
        /// 经 TurboJPEG 压缩后的独立 JPEG 字节数据
        /// </summary>
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 屏幕帧数据包（推送至 Master）
    /// </summary>
    public class ScreenFramePacketDto
    {
        /// <summary>
        /// 连续递增的帧序号
        /// </summary>
        public long FrameIndex { get; set; }

        /// <summary>
        /// 生成时间戳 (毫秒)
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// 物理显示器总宽度
        /// </summary>
        public int DisplayWidth { get; set; }

        /// <summary>
        /// 物理显示器总高度
        /// </summary>
        public int DisplayHeight { get; set; }

        /// <summary>
        /// 是否为全量关键帧 (I-Frame/Full Refresh)
        /// </summary>
        public bool IsKeyFrame { get; set; }

        /// <summary>
        /// 本帧变动的脏矩形图像块集合
        /// </summary>
        public List<DirtyBlockDto> Blocks { get; set; } = new();
    }

    /// <summary>
    /// 屏幕显示器拓扑与分辨率信息 DTO
    /// </summary>
    public class ScreenTopologyDto
    {
        public int DisplayIndex { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; } = 60;
        public int BitsPerPixel { get; set; } = 32;
        public double ScaleFactor { get; set; } = 1.0;
    }

    /// <summary>
    /// 屏幕捕获与编码参数设置 DTO
    /// </summary>
    public class CaptureOptionsDto
    {
        /// <summary>
        /// 最大捕获帧率限制 (10~60 FPS)
        /// </summary>
        public int MaxFps { get; set; } = 30;

        /// <summary>
        /// TurboJPEG 压缩质量 (1~100)
        /// </summary>
        public int JpegQuality { get; set; } = 75;

        /// <summary>
        /// 选中的显示器索引
        /// </summary>
        public int TargetDisplayIndex { get; set; } = 0;

        /// <summary>
        /// 像素色彩格式（默认 Rgb24，去掉 Alpha 通道；可选 Rgb565 极速低功耗）
        /// </summary>
        public PixelFormatMode PixelFormat { get; set; } = PixelFormatMode.Rgb24;
    }

    /// <summary>
    /// 鼠标动作事件 DTO
    /// </summary>
    public class MouseEventDto
    {
        /// <summary>
        /// 绝对坐标 X (0 ~ 65535 归一化坐标)
        /// </summary>
        public ushort NormalizedX { get; set; }

        /// <summary>
        /// 绝对坐标 Y (0 ~ 65535 归一化坐标)
        /// </summary>
        public ushort NormalizedY { get; set; }

        /// <summary>
        /// 鼠标动作类型组合标志
        /// </summary>
        public MouseActionFlags Flags { get; set; }

        /// <summary>
        /// 滚轮滚动增量 (正数向上滚动，负数向下滚动，通常为 120 的倍数)
        /// </summary>
        public int WheelDelta { get; set; }
    }

    /// <summary>
    /// 键盘按键事件 DTO
    /// </summary>
    public class KeyEventDto
    {
        /// <summary>
        /// 虚拟按键代码 (Win32 Virtual-Key Code)
        /// </summary>
        public ushort VirtualKey { get; set; }

        /// <summary>
        /// 硬件扫描码 (Hardware Scan Code)
        /// </summary>
        public ushort ScanCode { get; set; }

        /// <summary>
        /// 是否为按下状态 (true: KeyDown, false: KeyUp)
        /// </summary>
        public bool IsKeyDown { get; set; }

        /// <summary>
        /// 是否为扩展按键 (如右 Alt、方向键、小键盘等)
        /// </summary>
        public bool IsExtendedKey { get; set; }
    }

    /// <summary>
    /// Slave 节点上线注册报文 DTO
    /// </summary>
    public class SlaveNodeRegistrationDto
    {
        /// <summary>
        /// Slave 节点唯一标识
        /// </summary>
        public string SlaveId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 受控机名称
        /// </summary>
        public string MachineName { get; set; } = Environment.MachineName;

        /// <summary>
        /// 操作系统版本信息
        /// </summary>
        public string OsVersion { get; set; } = Environment.OSVersion.ToString();

        /// <summary>
        /// 当前选择的分享模式 (ViewOnly 或 FullControl)
        /// </summary>
        public ScreenShareMode ShareMode { get; set; }

        /// <summary>
        /// 屏幕物理分辨率宽度
        /// </summary>
        public int DisplayWidth { get; set; }

        /// <summary>
        /// 屏幕物理分辨率高度
        /// </summary>
        public int DisplayHeight { get; set; }

        /// <summary>
        /// 备注信息或标签
        /// </summary>
        public string Note { get; set; } = string.Empty;
    }
}
