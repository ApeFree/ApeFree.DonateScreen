# ApeFree.DonateScreen

<p align="center">
  <img src="https://img.shields.io/badge/ApeFree-DonateScreen-blue.svg" alt="ApeFree.DonateScreen" />
  <img src="https://img.shields.io/badge/.NET-10.0-blue.svg" alt=".NET 10" />
  <img src="https://img.shields.io/badge/License-Apache--2.0-green.svg" alt="License" />
</p>

## 项目简介

`ApeFree.DonateScreen` 是 ApeFree 系列旗下基于点对点主动分享理念打造的高性能远程屏幕分享与控制核心契约类库。
其技术方案完全延续 `ApeFree.DonateDisk` 的直连设计，通过内网受控端主动连接主控端，结合 DXGI 显存帧截取、脏矩形（Dirty Rects）差量裁剪、TurboJPEG SIMD 汇编极速编解码以及 Win32 硬件级输入模拟，为远程桌面应用提供开箱即用、低延迟、零 GC 抖动的核心基础设施。

---

## 🌟 核心特性

- **点对点全双工反向 RPC 代理**：基于 `ApeFree.ApeRpc.BytesIO` 与 `STTech.BytesIO`，受控端主动连出，主控端透明反向调用，免中心服务器。
- **GPU 显存级屏幕抓取**：基于 DXGI Desktop Duplication API，单帧抓取耗时 < 2ms，支持 60 FPS 稳定传输，内置 GDI 容灾降级机制。
- **脏矩形差量分块**：原生提取变动区域（Dirty Rects），配合空间合并算法合并细碎小块，静态画面 0 带宽消耗。
- **TurboJPEG SIMD 极速编码**：利用 LibJpeg-Turbo (AVX2/SSE2) 硬件加速，局部小块压缩耗时 < 0.3ms，无视频流编码累积延迟。
- **双契约接口物理权限隔离**：`IScreenShareService` 与 `IInputControlService` 完全解耦，支持完全控制与仅观看屏幕双模式。
- **高精度键鼠模拟与坐标映射**：基于 Win32 `SendInput`，支持绝对归一化坐标换算、键盘硬件扫描码及一键按键复位。

---

## 📦 安装方式

```bash
dotnet add package ApeFree.DonateScreen
```

---

## 🚀 快速上手

### 1. 受控端 (Slave) 开启屏幕与键鼠分享

```csharp
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Slave;

// 1. 配置 Slave 节点参数
var options = new ScreenSlaveOptions
{
    MasterHost = "192.168.1.100",
    MasterPort = 9822,
    ShareMode = ScreenShareMode.FullControl, // 或 ScreenShareMode.ViewOnly (仅观看)
    NodeName = "MyOfficePC",
    CaptureOptions = new CaptureOptionsDto { MaxFps = 30, JpegQuality = 75 }
};

// 2. 启动节点主动连接 Master
using var slaveNode = new ScreenSlaveNode(options);
bool connected = await slaveNode.StartAsync();
Console.WriteLine(connected ? "已成功连接至 Master 并开启屏幕分享" : "连接失败，正在后台自动重连...");
```

### 2. 主控端 (Master) 接收屏幕并实施控制

```csharp
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.Rendering;

// 1. 启动 Master 监听服务
var masterOptions = new ScreenMasterOptions { ListenPort = 9822 };
using var masterNode = new ScreenMasterNode(masterOptions);
await masterNode.StartAsync();

// 2. 监听 Slave 接入事件
masterNode.SessionManager.SlaveConnected += (sender, session) =>
{
    Console.WriteLine($"Slave 设备上线: {session.Info.MachineName} ({session.RemoteAddress})");
    Console.WriteLine($"分享模式: {session.Info.ShareMode}, 分辨率: {session.Info.DisplayWidth}x{session.Info.DisplayHeight}");

    // 监听实时屏幕帧并渲染
    var blitter = new WriteableBitmapBlitter();
    session.FrameReceived += (s, framePacket) =>
    {
        // 在 UI 线程通过 WriteableBitmap 局部增量渲染
        Application.Current.Dispatcher.Invoke(() => blitter.RenderPacket(framePacket));
    };

    // 若具备控制权限，可直接注入键盘鼠标指令
    if (session.CanControl && session.InputService != null)
    {
        session.InputService.SendMouseEvent(new MouseEventDto
        {
            NormalizedX = 32768, // 屏幕中心 X (0~65535)
            NormalizedY = 32768, // 屏幕中心 Y (0~65535)
            Flags = MouseActionFlags.Move
        });
    }
};
```

---

## 🧩 关联子工程

| 项目名称 | 说明 |
| :--- | :--- |
| **`ApeFree.DonateScreen.WinSlaveDesktop`** | WPF 专用受控分享端桌面应用程序 |
| **`ApeFree.DonateScreen.WinDesktop`** | WPF 综合主控/受控端桌面应用程序（包含多设备管理卡片与控制视窗） |

---

## 📄 开源许可证

本项目基于 [Apache-2.0](LICENSE) 许可证开源。
