# ApeFree.DonateScreen

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue.svg" alt=".NET Version" />
  <img src="https://img.shields.io/badge/Platform-Windows-lightgrey.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/License-Apache--2.0-green.svg" alt="License" />
</p>

## 📖 项目简介

`ApeFree.DonateScreen` 是一个面向 Windows 平台的高性能**主动式远程屏幕分享与控制系统**。
其设计哲学承袭自开源项目 `ApeFree.DonateDisk` 与 `ApeFree.DonateProcess`：
- **网络与角色彻底解耦**：两端地位平等，受控端（Slave）既可主动反向拨号连入 Master（天然穿透 NAT 与防火墙），也可作为正向受控服务开放本地端口监听（**支持多个 Master 客户端并发连接观看与独立控制**）；主控端（Master）既可开放监听等待 Slave 连入，也可主动直连指定 Slave 开展控制；
- **ApeRpc 全双工透明代理**：通过 `ApeFree.ApeRpc.BytesIO`，在底层直连 TCP 信道建立后，主控端透明获取受控端暴露的业务接口代理，实现毫秒级反向调用与事件推送；
- **DXGI GPU 显存级屏幕捕获**：基于 `ScreenCapture.NET` (DXGI Desktop Duplication API)，直接访问 GPU 显存表面，帧捕获延迟 < 2ms，画面静止时零开销跳过；
- **FFmpeg H.264 超低延迟流式编解码**：进程内原生集成 FFmpeg libavcodec (`x264` + `zerolatency` + `ultrafast`)，单帧压缩比达 99.8%，1080P P 帧仅数十字节，端到端编码耗时仅 10ms 级，原生兼容 Windows 任意缩放奇数分辨率（自动偶数规范化）；
- **单调配额流控机制 (Flow Control)**：主控端驱动配额步进，丢弃陈旧队列并按需触发 IDR 关键帧，彻底杜绝画面积压延迟；
- **WPF 局部增量渲染与零 GC 优化**：主控端通过常驻 `WriteableBitmap` 内存锁定与 `AddDirtyRect` 局部内存拷贝，实现丝滑流畅的 60 FPS 硬件合成显示；
- **权限细粒度物理隔离**：屏幕传输与键盘鼠标控制分为两个独立的服务契约接口，Slave 端可自主选择【完全控制】或【仅观看屏幕 (只读)】模式。

---

## 🏛️ 工程套件划分

| 工程目录 / 库名称 | 类型 | 职责说明 |
| :--- | :--- | :--- |
| **`ApeFree.DonateScreen`** | 核心类库 | 跨端通信契约 (`IScreenShareService`, `IInputControlService`)、DXGI 捕获管道、FFmpeg H.264 / TurboJPEG 编解码、Win32 `SendInput` 键鼠模拟、双向传输节点 (`ScreenMasterNode`, `ScreenSlaveNode`) 与多 Slave 会话管理器 (`SlaveSessionManager`) |
| **`ApeFree.DonateScreen.WinSlaveDesktop`** | WPF 桌面应用 | 受控端桌面程序，支持【反向连向 Master (Connect)】与【开放本地监听 (Listen)】双模式，支持【完全控制】与【仅观看屏幕】模式切换、画质微调及紧急切断 |
| **`ApeFree.DonateScreen.WinDesktop`** | WPF 桌面应用 | 综合主控/受控桌面程序，内置本地监听 (Listen) 与主动直连 (Connect) 双入口，多 Slave 设备卡片管理、独立远程交互控制视窗及复用受控功能 |
| **`ApeFree.DonateScreen.Tests`** | 测试工程 | FFmpeg H.264 编解码往返验证、DXGI 容灾降级测试、全双工 RPC 远程控制、正向直连/反向穿透解耦测试及多 Master 并发接入测试 |

---

## 🚀 核心架构与 2x2 灵活拓扑模型

```
【拓扑 A：正向直连模式 (推荐：受控端开放端口，支持多个 Master 并发连入控制/观看)】
Master 1 (Client 主动连出) ──┐
                             ├────[TCP 直连]────> Slave 共享节点 (TcpServer 本地监听 9822)
Master 2 (Client 主动连出) ──┘                    (DXGI GPU 采集 + FFmpeg H.264 编码 + SendInput)

【拓扑 B：反向受控模式 (受控端位于复杂内网，主动向 Master 反弹连接)】
Master 主控端 (TcpServer 监听 9822) <──────[TCP 连入]────── Slave 受控端 (Client 连出)
```

---

## 💻 快速上手指南

### 1. 方案 A：正向直连模式 (受控端开放服务，主控端直连，支持多端)
1. **受控端 (WinSlaveDesktop)**：
   - 网络通信模式勾选【正向受控服务 (Listen)】；
   - 设置本地监听端口（默认 `9822`）；
   - 选择权限模式（完全控制 / 仅观看屏幕）与画质；
   - 点击【启动监听 (Listen)】即可就绪等待控制端。
2. **主控端 (WinDesktop)**：
   - 在“主动直连 (正向受控)”区域输入受控端的 IP 与端口；
   - 点击【连接目标】；
   - 目标设备立即出现在卡片列表中，点击【开始控制】即可打开交互视窗。

### 2. 方案 B：反向穿透模式 (受控端位于内网，主动连出)
1. **主控端 (WinDesktop)**：
   - 在“本地监听 (反向受控)”区域确认端口（默认 `9822`）；
   - 点击【启动监听 (Listen)】。
2. **受控端 (WinSlaveDesktop)**：
   - 网络通信模式勾选【反向连向 Master (Connect)】；
   - 输入主控端的 IP 地址与端口号；
   - 点击【开始分享 (Connect)】即可反向接入主控端。

---

## 📄 开源许可证

本项目基于 [Apache-2.0](LICENSE) 开源许可证发布。
