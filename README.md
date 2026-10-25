# ApeFree.DonateScreen

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue.svg" alt=".NET Version" />
  <img src="https://img.shields.io/badge/Platform-Windows-lightgrey.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/License-Apache--2.0-green.svg" alt="License" />
</p>

## 📖 项目简介

`ApeFree.DonateScreen` 是一个面向 Windows 平台的高性能**主动捐赠式远程屏幕分享与控制系统**。
其设计哲学完全承袭自开源项目 `ApeFree.DonateDisk`：
- **突破传统远程桌面困境**：受控端（Slave）主动发起 TCP 连接至指定主控端（Master），无需公网 IP、无需配置内网穿透与路由器端口映射，彻底摆脱第三方中心化云服务器的限速与隐私顾虑；
- **ApeRpc 全双工反向透明代理**：通过 `ApeFree.ApeRpc.BytesIO`，在底层直连 TCP 信道建立后，主控端透明获取受控端暴露的业务接口代理，实现毫秒级反向调用与事件推送；
- **DXGI GPU 显存级屏幕捕获**：基于 `ScreenCapture.NET` (DXGI Desktop Duplication API)，直接访问 GPU 显存表面，帧捕获延迟 < 2ms，画面静止时零开销跳过；
- **FFmpeg H.264 超低延迟流式编解码**：进程内原生集成 FFmpeg libavcodec (`x264` + `zerolatency` + `ultrafast`)，单帧压缩比达 99.8%，1080P P 帧仅数十字节，端到端编码耗时仅 10ms 级；
- **单调配额流控机制 (Flow Control)**：主控端驱动配额步进，丢弃陈旧队列并按需触发 IDR 关键帧，彻底杜绝画面积压延迟；
- **WPF 局部增量渲染与零 GC 优化**：主控端通过常驻 `WriteableBitmap` 内存锁定与 `AddDirtyRect` 局部内存拷贝，实现丝滑流畅的 60 FPS 硬件合成显示；
- **权限细粒度物理隔离**：屏幕传输与键盘鼠标控制分为两个独立的服务契约接口，Slave 端可自主选择【完全控制】或【仅观看屏幕 (只读)】模式。

---

## 🏛️ 工程套件划分

| 工程目录 / 库名称 | 类型 | 职责说明 |
| :--- | :--- | :--- |
| **`ApeFree.DonateScreen`** | 核心类库 | 跨端通信契约 (`IScreenShareService`, `IInputControlService`)、DXGI 捕获管道、FFmpeg H.264 / TurboJPEG 编解码、Win32 `SendInput` 键鼠模拟与多 Slave 会话管理器 (`SlaveSessionManager`) |
| **`ApeFree.DonateScreen.WinSlaveDesktop`** | WPF 桌面应用 | 受控端桌面程序，仅提供将本地屏幕与键鼠控制权分享给 Master，支持【完全控制】与【仅观看屏幕】模式切换、画质与 CRF 调节及紧急切断 |
| **`ApeFree.DonateScreen.WinDesktop`** | WPF 桌面应用 | 综合主控/受控桌面程序，引用 `WinSlaveDesktop` 复用受控功能；内置 TCP 监听服务，多 Slave 设备列表管理及高性能远程交互控制视窗 |
| **`ApeFree.DonateScreen.Tests`** | 测试工程 | FFmpeg H.264 编解码往返验证、DXGI 容灾降级测试、全双工 RPC 远程控制与仅观看权限阻断端到端测试 |

---

## 🚀 核心架构与交互模型

```
┌────────────────────────────────────────────────────────┐
│             Slave 计算机 (受控端 / 发起连接)           │
│  DXGI (GPU 显存) ──► FFmpeg H.264 (超低延迟编码)       │
│                                           │            │
│  Win32 SendInput ◄── IInputControlService ◄ (H.264 NAL)│
│  (模拟键盘鼠标)      (在完全控制模式下暴露) │            │
│                                           ▼            │
│                              ApeRpc.BytesIO (TcpClient)│
└───────────────────────────────────────────┬────────────┘
                                            │ TCP 直连全双工信道 (单端口)
┌───────────────────────────────────────────▼────────────┐
│             Master 计算机 (主控端 / 监听服务)          │
│                              ApeRpc.BytesIO (TcpServer)│
│                                           │            │
│  WPF WriteableBitmap ◄── FFmpeg H.264 解码 ┤            │
│  WPF RemoteDisplayBox (捕获用户键盘鼠标) ─┘            │
└────────────────────────────────────────────────────────┘
```

---

## 💻 快速上手指南

### 1. 受控端 (WinSlaveDesktop)
1. 启动 `ApeFree.DonateScreen.WinSlaveDesktop.exe`；
2. 输入主控端机器的 IP 地址与端口号（默认端口 `9822`）；
3. 选择分享权限模式：
   - **完全控制**：Master 可实时观看屏幕并操作键盘鼠标；
   - **仅观看屏幕**：Master 仅能观看屏幕，拒绝一切键盘鼠标控制指令；
4. 选择编码方案（默认推荐 **FFmpeg H.264**）与 CRF 质量；
5. 点击【开始分享 (Connect)】即可连入 Master。受控机随时可点击【紧急切断控制】收回本地物理控制权。

### 2. 主控端 (WinDesktop)
1. 启动 `ApeFree.DonateScreen.WinDesktop.exe`；
2. 在“远程桌面管理 (Master)”标签页设置监听端口（默认 `9822`），点击【启动监听 (Listen)】；
3. 当远端 Slave 接入后，设备列表将实时出现其卡片（包含机器名、IP、分辨率、分享模式标签）；
4. 点击卡片上的【开始控制】按钮，即可打开独立的远程桌面交互视窗，畅享低延迟实时控制体验；
5. 在“本机受控分享 (Slave)”标签页，本机也可同时作为受控端分享给其他主机。

---

## 📄 开源许可证

本项目基于 [Apache-2.0](LICENSE) 开源许可证发布。
