using ApeFree.DonateScreen.Contracts;
using STTech.BytesIO.Core;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 端维护的单个已接入 Slave 会话对象
    /// </summary>
    public class SlaveSession : INotifyPropertyChanged, IDisposable
    {
        private readonly object _eventLock = new();
        private SlaveNodeRegistrationDto _info;
        private IScreenShareService? _screenService;
        private IInputControlService? _inputService;
        private EventHandler<ScreenFramePacketDto>? _frameReceived;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SessionId { get; }
        public BytesClient Client { get; }
        public string RemoteAddress { get; }
        public DateTime ConnectedTime { get; } = DateTime.Now;

        /// <summary>
        /// Slave 上报的注册元数据
        /// </summary>
        public SlaveNodeRegistrationDto Info
        {
            get => _info;
            internal set
            {
                _info = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanControl));
            }
        }

        /// <summary>
        /// 远程屏幕服务透明代理
        /// </summary>
        public IScreenShareService? ScreenService
        {
            get => _screenService;
            internal set
            {
                _screenService = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanControl));
            }
        }

        /// <summary>
        /// 远程键盘鼠标控制服务透明代理（在仅观看模式下为 null）
        /// </summary>
        public IInputControlService? InputService
        {
            get => _inputService;
            internal set
            {
                _inputService = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanControl));
            }
        }

        /// <summary>
        /// 是否具备键盘鼠标控制权限
        /// </summary>
        public bool CanControl => InputService != null && Info.ShareMode == ScreenShareMode.FullControl;

        /// <summary>
        /// 屏幕帧到达事件（按需动态订阅，仅在有监听者时向远端注册事件，避免空闲消耗流量与 CPU）
        /// </summary>
        public event EventHandler<ScreenFramePacketDto>? FrameReceived
        {
            add
            {
                lock (_eventLock)
                {
                    bool wasEmpty = _frameReceived == null;
                    _frameReceived += value;
                    if (wasEmpty && _screenService != null)
                    {
                        try { _screenService.FrameArrived += OnFrameArrived; } catch { }
                    }
                }
            }
            remove
            {
                lock (_eventLock)
                {
                    _frameReceived -= value;
                    if (_frameReceived == null && _screenService != null)
                    {
                        try { _screenService.FrameArrived -= OnFrameArrived; } catch { }
                    }
                }
            }
        }

        public SlaveSession(string sessionId, BytesClient client, string remoteAddress, SlaveNodeRegistrationDto? initialInfo = null)
        {
            SessionId = sessionId;
            Client = client;
            RemoteAddress = remoteAddress;
            _info = initialInfo ?? new SlaveNodeRegistrationDto
            {
                SlaveId = sessionId,
                MachineName = remoteAddress,
                ShareMode = ScreenShareMode.FullControl
            };
        }

        internal void BindScreenService(IScreenShareService screenService)
        {
            lock (_eventLock)
            {
                if (_screenService != null && _frameReceived != null)
                {
                    try { _screenService.FrameArrived -= OnFrameArrived; } catch { }
                }

                _screenService = screenService;

                if (_screenService != null && _frameReceived != null)
                {
                    try { _screenService.FrameArrived += OnFrameArrived; } catch { }
                }
            }

            OnPropertyChanged(nameof(ScreenService));
            OnPropertyChanged(nameof(CanControl));
        }

        private void OnFrameArrived(object? sender, ScreenFramePacketDto e)
        {
            _frameReceived?.Invoke(this, e);
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            lock (_eventLock)
            {
                if (_screenService != null && _frameReceived != null)
                {
                    try { _screenService.FrameArrived -= OnFrameArrived; } catch { }
                }
                _screenService = null;
                _frameReceived = null;
            }

            InputService = null;
            OnPropertyChanged(nameof(ScreenService));
            OnPropertyChanged(nameof(InputService));
            OnPropertyChanged(nameof(CanControl));
        }
    }
}
