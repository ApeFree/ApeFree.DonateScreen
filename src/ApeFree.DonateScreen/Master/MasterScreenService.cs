using ApeFree.DonateScreen.Contracts;
using System;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 节点协同与注册服务实现（由 Master 暴露供 Slave 上线通知）
    /// </summary>
    public class MasterScreenService : IMasterScreenService
    {
        /// <summary>
        /// 收到 Slave 注册报文事件
        /// </summary>
        public event EventHandler<SlaveNodeRegistrationDto>? SlaveRegistered;

        /// <summary>
        /// 收到 Slave 心跳事件
        /// </summary>
        public event EventHandler<(string slaveId, long timestamp)>? HeartbeatReceived;

        public void RegisterSlaveNode(SlaveNodeRegistrationDto info)
        {
            if (info == null) return;
            SlaveRegistered?.Invoke(this, info);
        }

        public void Heartbeat(string slaveId, long timestamp)
        {
            HeartbeatReceived?.Invoke(this, (slaveId, timestamp));
        }
    }
}
