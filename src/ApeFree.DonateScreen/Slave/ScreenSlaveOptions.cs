using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.P2P;
using System.Collections.Generic;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// Slave 节点配置参数
    /// </summary>
    public class ScreenSlaveOptions
    {
        /// <summary>
        /// 网络通信传输模式：Connect 主动连向 Master；Listen 开放本地端口作为正向受控服务 (默认 Connect)
        /// </summary>
        public NetworkTransportMode TransportMode { get; set; } = NetworkTransportMode.Connect;

        /// <summary>
        /// 目标 Master 端主机地址 (IP 或域名，Connect 模式生效)
        /// </summary>
        public string MasterHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// 目标 Master 端监听端口 (Connect 模式生效，默认 9822)
        /// </summary>
        public int MasterPort { get; set; } = 9822;

        /// <summary>
        /// 本地服务端监听主机绑定地址 (Listen 模式生效，默认 0.0.0.0)
        /// </summary>
        public string ListenHost { get; set; } = "0.0.0.0";

        /// <summary>
        /// 本地服务端监听端口 (Listen 模式生效，默认 9822)
        /// </summary>
        public int ListenPort { get; set; } = 9822;

        /// <summary>
        /// 分享模式：ViewOnly (仅观看屏幕) 或 FullControl (完全控制)
        /// </summary>
        public ScreenShareMode ShareMode { get; set; } = ScreenShareMode.FullControl;

        /// <summary>
        /// 初始采集与压缩参数
        /// </summary>
        public CaptureOptionsDto CaptureOptions { get; set; } = new CaptureOptionsDto();

        /// <summary>
        /// Slave 节点展示名称
        /// </summary>
        public string NodeName { get; set; } = string.Empty;

        /// <summary>
        /// 是否开启断线自动重连 (Connect 模式生效，默认 true)
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔时间 (毫秒，默认 3000ms)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 3000;

        /// <summary>
        /// 是否启用 P2P (Libp2p) 去中心化中继模式
        /// </summary>
        public bool UseP2P { get; set; }

        /// <summary>
        /// P2P 引导与中继服务端地址（例如 127.0.0.1:9832 或公共中继）
        /// </summary>
        public string? P2PServerAddress { get; set; }

        /// <summary>
        /// 目标 Master 节点的 PeerId (P2P 模式下必须提供以完成精准寻址与会话配对)
        /// </summary>
        public string? TargetMasterPeerId { get; set; }

        /// <summary>
        /// 本地节点的身份私钥种子（可选）
        /// </summary>
        public string? IdentitySeed { get; set; }

        /// <summary>
        /// P2P 中继服务端的 PeerId（可选）
        /// </summary>
        public string? ServerPeerId { get; set; }

        /// <summary>
        /// P2P 中继服务端的凭据种子（可选）
        /// </summary>
        public string? ServerSeed { get; set; }

        /// <summary>
        /// 是否启用 Relay 协议
        /// </summary>
        public bool EnableRelay { get; set; } = false;

        /// <summary>
        /// 是否启用 DHT 节点发现
        /// </summary>
        public bool EnableDht { get; set; } = true;

        /// <summary>
        /// 是否启用 mDNS 局域网节点发现
        /// </summary>
        public bool EnableMdns { get; set; } = true;

        /// <summary>
        /// 是否启用 QUIC 传输
        /// </summary>
        public bool EnableQuic { get; set; } = false;

        /// <summary>
        /// 引导节点 Multiaddr 列表
        /// </summary>
        public List<string> BootstrapPeers { get; set; } = new List<string>();
    }
}
