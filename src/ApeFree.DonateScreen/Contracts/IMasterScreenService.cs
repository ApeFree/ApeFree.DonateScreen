namespace ApeFree.DonateScreen.Contracts
{
    /// <summary>
    /// Master 节点协同与受控端注册服务契约。
    /// 由 Master 端实现并暴露，Slave 端在接入后主动通知 Master 节点登记受控设备信息。
    /// </summary>
    public interface IMasterScreenService
    {
        /// <summary>
        /// Slave 节点向 Master 注册自身设备信息与共享模式
        /// </summary>
        /// <param name="info">Slave 设备基本信息（机器名、操作系统、分享模式、屏幕分辨率等）</param>
        void RegisterSlaveNode(SlaveNodeRegistrationDto info);

        /// <summary>
        /// 心跳保活与状态汇报
        /// </summary>
        /// <param name="slaveId">Slave 设备唯一标识</param>
        /// <param name="timestamp">客户端本地时间戳</param>
        void Heartbeat(string slaveId, long timestamp);
    }
}
