# ApeFree.DonateScreen

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue.svg" alt=".NET Version" />
  <img src="https://img.shields.io/badge/Platform-Windows-lightgrey.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/License-Apache--2.0-green.svg" alt="License" />
</p>

## 📖 项目简介

`ApeFree.DonateScreen` 是一个面向 Windows 平台的高性能**主动捐赠式远程屏幕分享与控制系统**。
其设计哲学完全承袭自开源项目 `ApeFree.DonateDisk`：
- **突破传统远程桌面困境**：支持**标准 TCP 直连**与 **P2P 去中心化中继 (Libp2p Circuit Relay)** 双重网络架构。受控端（Slave）主动发起连接至主控端（Master）或 P2P 中继节点，双方均无需公网 IP、无需配置路由器端口映射，彻底摆脱第三方中心化云服务器的限速与隐私顾虑；
- **P2P 精准寻址与安全会话配对**：Master 端展示自身稳定 PeerId，Slave 指定目标 Master PeerId 发起配对，中继端精准建立专用点对点通道定向互传，无广播开销且杜绝多设备串扰；
- **ApeRpc 全双工反向透明代理**：通过 `ApeFree.ApeRpc.BytesIO` 与 `STTech.BytesIO`，在底层直连或 P2P 信道建立后，主控端透明获取受控端暴露的业务接口代理，实现毫秒级反向调用与事件推送；
- **DXGI GPU 显存级屏幕捕获**：基于 `ScreenCapture.NET` (DXGI Desktop Duplication API)，直接访问 GPU 显存表面，帧捕获延迟 < 2ms，画面静止时零开销跳过；
- **FFmpeg H.264 超低延迟流式编解码**：进程内原生集成 FFmpeg libavcodec (`x264` + `zerolatency` + `ultrafast`)，单帧压缩比达 99.8%，1080P P 帧仅数十字节，端到端编码耗时仅 10ms 级；
- **单调配额流控机制 (Flow Control)**：主控端驱动配额步进，丢弃陈旧队列并按需触发 IDR 关键帧，彻底杜绝画面积压延迟；
- **WPF 局部增量渲染与零 GC 优化**：主控端通过常驻 `WriteableBitmap` 内存锁定与 `AddDirtyRect` 局部内存拷贝，实现丝滑流畅的 60 FPS 硬件合成显示；
- **权限细粒度物理隔离**：屏幕传输与键盘鼠标控制分为两个独立的服务契约接口，Slave 端可自主选择【完全控制】或【仅观看屏幕 (只读)】模式。

---

## 🏛️ 工程套件划分

| 工程目录 / 库名称 | 类型 | 职责说明 |
| :--- | :--- | :--- |
| **`ApeFree.DonateScreen`** | 核心类库 | 跨端通信契约 (`IScreenShareService`, `IInputControlService`)、P2P 协议与中继宿主 (`ScreenRelayServer`, `P2PRelayManager`)、DXGI 捕获管道、FFmpeg H.264 / TurboJPEG 编解码、Win32 `SendInput` 键鼠模拟与多 Slave 会话管理器 (`SlaveSessionManager`) |
| **`ApeFree.DonateScreen.WinSlaveDesktop`** | WPF 桌面应用 | 受控端桌面程序，支持 TCP 直连与 P2P 中继模式切换；输入目标 Master PeerId 即可一键建立跨 NAT 屏幕与键鼠分享；提供【完全控制】与【仅观看屏幕】模式切换、画质调节及紧急切断 |
| **`ApeFree.DonateScreen.WinDesktop`** | WPF 桌面应用 | 综合主控/受控桌面程序，包含三个核心功能标签页：<br>1. **远程桌面管理 (Master)**：支持标准 TCP 监听或 P2P 中继接入，展示本机稳定 PeerId，管理接入的设备卡片并打开低延迟远程控制视窗；<br>2. **本机受控分享 (Slave)**：嵌入受控面板；<br>3. **P2P 中继服务 (Relay Server)**：内置独立公网/局域网 P2P 引导与中继服务管理面板 |
| **`ApeFree.DonateScreen.Tests`** | 测试工程 | 包含 P2P 工具函数测试、会话精准配对隔离测试、FFmpeg H.264 往返测试及 P2P 屏幕推流端到端测试 |

---

## 🚀 核心架构拓扑

### 1. P2P 去中心化中继模式 (双方均位于 NAT 内部，跨公网首选)
```
                    ┌───────────────────────────────────┐
                    │     公网 / 本机 P2P Relay Server    │
                    │      (STTech.BytesIO.P2P)         │
                    │  [Target PeerId Session Pairing]  │
                    └────────▲─────────────────▲────────┘
          主动连接 (Multiaddr)│                 │主动连接 (Multiaddr + TargetPeerId)
                             │                 │
 ┌───────────────────────────┴─┐             ┌─┴───────────────────────────┐
 │   Master 端 (主控端 / NAT)  │             │    Slave 端 (受控端 / NAT)  │
 │  展示自身稳定 PeerId        │ <─────────> │   指定目标 Master PeerId    │
 │  WPF 远程控制 & 画面解码    │   ApeRpc    │   DXGI 抓屏 + H.264 增量推流│
 └─────────────────────────────┘             └─────────────────────────────┘
```

### 2. 标准 TCP 直连模式 (局域网 / 单向公网直连)
```
Master (TcpServer 监听 9822) <──────[TCP 直连]────── Slave (TcpClient 拨号)
       │                                                    │
WPF 远程画面显示与输入控制                             DXGI 抓屏 + H.264 编码
```

---

## 💻 快速上手指南

### 方案 A：P2P 中继穿透模式 (跨公网/复杂 NAT 穿透首选，推荐)

1. **启动中继服务 (Relay Server)**：
   - 可以在具有公网 IP 的服务器上部署，或者在任意一台 `WinDesktop` 的【P2P 中继服务 (Relay Server)】标签页点击【启动中继服务】（默认端口 `9832`）。
2. **主控端 (WinDesktop)**：
   - 进入【远程桌面管理 (Master)】标签页；
   - 网络模式选择【P2P 中继穿透模式】；
   - 填写中继服务端地址（如 `127.0.0.1:9832` 或公网中继 IP:端口）；
   - 点击【复制 PeerId】将本机展示的稳定 PeerId 复制发送给受控端；
   - 点击【启动 P2P 服务】开始等待接入。
3. **受控端 (WinSlaveDesktop)**：
   - 启动受控端程序，网络模式选择【P2P 中继穿透模式】；
   - 填入相同的 P2P 中继服务端地址；
   - 在【目标 Master PeerId】输入框中粘贴主控端的 PeerId；
   - 选择分享权限（完全控制或仅观看屏幕），点击【开始分享 (Connect)】；
   - 连接成功后，主控端界面将立即弹出该设备卡片，点击【开始控制】即可畅享毫秒级远程控制！

---

### 方案 B：标准 TCP 直连模式 (局域网 / 主控端有公网 IP)

1. **主控端 (WinDesktop)**：
   - 网络模式切换为【标准 TCP 监听模式】，设置端口（默认 `9822`），点击【启动监听】；
2. **受控端 (WinSlaveDesktop)**：
   - 网络模式切换为【标准 TCP 直连模式】，输入主控端 IP 地址与端口，点击【开始分享】。

---

## 📄 开源许可证

本项目基于 [Apache-2.0](LICENSE) 开源许可证发布。
