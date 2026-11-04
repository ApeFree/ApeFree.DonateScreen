namespace ApeFree.DonateScreen.Contracts
{
    /// <summary>
    /// 网络通信传输模式
    /// </summary>
    public enum NetworkTransportMode
    {
        /// <summary>
        /// 服务端监听模式 (Listen)：开放本地端口，等待对端连入
        /// </summary>
        Listen = 0,

        /// <summary>
        /// 客户端连接模式 (Connect)：主动向目标主机与端口发起 TCP 连接
        /// </summary>
        Connect = 1
    }
}
