using ApeFree.DonateScreen.Contracts;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// Slave 节点配置参数
    /// </summary>
    public class ScreenSlaveOptions
    {
        /// <summary>
        /// 目标 Master 端主机地址 (IP 或域名)
        /// </summary>
        public string MasterHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// 目标 Master 端监听端口 (默认 9822)
        /// </summary>
        public int MasterPort { get; set; } = 9822;

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
        /// 是否开启断线自动重连 (默认 true)
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔时间 (毫秒，默认 3000ms)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 3000;
    }
}
