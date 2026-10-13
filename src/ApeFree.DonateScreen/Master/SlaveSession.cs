using ApeFree.DonateScreen.Contracts;
using STTech.BytesIO.Core;
using System;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 端维护的单个已接入 Slave 会话对象
    /// </summary>
    public class SlaveSession : IDisposable
    {
        public string SessionId { get; }
        public BytesClient Client { get; }
        public string RemoteAddress { get; }
        public DateTime ConnectedTime { get; } = DateTime.Now;

        /// <summary>
        /// Slave 上报的注册元数据
        /// </summary>
        public SlaveNodeRegistrationDto Info { get; internal set; }

        /// <summary>
        /// 远程屏幕服务透明代理
        /// </summary>
        public IScreenShareService? ScreenService { get; internal set; }

        /// <summary>
        /// 远程键盘鼠标控制服务透明代理（在仅观看模式下为 null）
        /// </summary>
        public IInputControlService? InputService { get; internal set; }

        /// <summary>
        /// 是否具备键盘鼠标控制权限
        /// </summary>
        public bool CanControl => InputService != null && Info.ShareMode == ScreenShareMode.FullControl;

        /// <summary>
        /// 屏幕帧到达事件
        /// </summary>
        public event EventHandler<ScreenFramePacketDto>? FrameReceived;

        public SlaveSession(string sessionId, BytesClient client, string remoteAddress, SlaveNodeRegistrationDto? initialInfo = null)
        {
            SessionId = sessionId;
            Client = client;
            RemoteAddress = remoteAddress;
            Info = initialInfo ?? new SlaveNodeRegistrationDto
            {
                SlaveId = sessionId,
                MachineName = remoteAddress,
                ShareMode = ScreenShareMode.FullControl
            };
        }

        internal void BindScreenService(IScreenShareService screenService)
        {
            if (ScreenService != null)
            {
                ScreenService.FrameArrived -= OnFrameArrived;
            }

            ScreenService = screenService;
            if (ScreenService != null)
            {
                ScreenService.FrameArrived += OnFrameArrived;
            }
        }

        private void OnFrameArrived(object? sender, ScreenFramePacketDto e)
        {
            FrameReceived?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (ScreenService != null)
            {
                ScreenService.FrameArrived -= OnFrameArrived;
                ScreenService = null;
            }
            InputService = null;
        }
    }
}
