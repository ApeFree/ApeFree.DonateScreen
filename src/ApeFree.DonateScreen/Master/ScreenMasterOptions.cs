using ApeFree.DonateScreen.Contracts;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 节点监听与配置参数
    /// </summary>
    public class ScreenMasterOptions
    {
        /// <summary>
        /// 网络通信传输模式：Listen 监听端口等待受控端连入；Connect 主动连向目标受控端 (默认 Listen)
        /// </summary>
        public NetworkTransportMode TransportMode { get; set; } = NetworkTransportMode.Listen;

        /// <summary>
        /// 本地监听绑定的 Host 地址 (Listen 模式生效，默认 0.0.0.0 监听所有网卡)
        /// </summary>
        public string ListenHost { get; set; } = "0.0.0.0";

        /// <summary>
        /// 本地服务监听端口 (Listen 模式生效，默认 9822)
        /// </summary>
        public int ListenPort { get; set; } = 9822;

        /// <summary>
        /// 目标 Slave 主机地址 (Connect 模式生效，默认 127.0.0.1)
        /// </summary>
        public string TargetHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// 目标 Slave 端口 (Connect 模式生效，默认 9822)
        /// </summary>
        public int TargetPort { get; set; } = 9822;

        /// <summary>
        /// Master 节点标识名称
        /// </summary>
        public string NodeName { get; set; } = "MasterNode";

        /// <summary>
        /// 是否开启断线自动重连 (Connect 模式生效，默认 true)
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔时间 (毫秒，默认 3000ms)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 3000;
    }
}
