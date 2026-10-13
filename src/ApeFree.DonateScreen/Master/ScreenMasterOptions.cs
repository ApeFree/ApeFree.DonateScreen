namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 节点监听与配置参数
    /// </summary>
    public class ScreenMasterOptions
    {
        /// <summary>
        /// 本地监听绑定的 Host 地址 (默认 0.0.0.0 监听所有网卡)
        /// </summary>
        public string ListenHost { get; set; } = "0.0.0.0";

        /// <summary>
        /// 本地服务监听端口 (默认 9822)
        /// </summary>
        public int ListenPort { get; set; } = 9822;

        /// <summary>
        /// Master 节点标识名称
        /// </summary>
        public string NodeName { get; set; } = "MasterNode";
    }
}
