using ApeFree.DonateScreen.P2P;
using System.Collections.Generic;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 节点监听与配置参数
    /// </summary>
    public class ScreenMasterOptions
    {
        /// <summary>
        /// 本地监听绑定的 Host 地址 (默认 0.0.0.0 监听所有网卡，仅用于标准 TCP 模式)
        /// </summary>
        public string ListenHost { get; set; } = "0.0.0.0";

        /// <summary>
        /// 本地服务监听端口 (默认 9822，仅用于标准 TCP 模式)
        /// </summary>
        public int ListenPort { get; set; } = 9822;

        /// <summary>
        /// Master 节点标识名称
        /// </summary>
        public string NodeName { get; set; } = "MasterNode";

        /// <summary>
        /// 是否启用 P2P (Libp2p) 去中心化中继模式
        /// </summary>
        public bool UseP2P { get; set; }

        /// <summary>
        /// P2P 引导与中继服务端地址（若指定则作为客户端连入公网中继；若留空则作为本地 P2P 服务端监听）
        /// </summary>
        public string? P2PServerAddress { get; set; }

        /// <summary>
        /// 本地 P2P 监听端口（默认 9832，当 P2PServerAddress 为空时使用）
        /// </summary>
        public int P2PPort { get; set; } = P2PAddressHelper.DefaultP2PPort;

        /// <summary>
        /// 本地节点的身份私钥种子（用于固定 Master 的 PeerId）
        /// </summary>
        public string? IdentitySeed { get; set; }

        /// <summary>
        /// P2P 服务端的 PeerId（可选）
        /// </summary>
        public string? ServerPeerId { get; set; }

        /// <summary>
        /// P2P 服务端的凭据种子（可选）
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
