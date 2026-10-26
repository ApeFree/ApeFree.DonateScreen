using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.P2P;
using ApeFree.DonateScreen.WinDesktop.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace ApeFree.DonateScreen.WinDesktop.ViewModels
{
    public class MasterViewModel : INotifyPropertyChanged
    {
        private ScreenMasterNode? _masterNode;
        private bool _isP2PMode = true;
        private bool _isTcpMode = false;
        private string _p2pServerAddress = "127.0.0.1:9832";
        private int _p2pPort = P2PAddressHelper.DefaultP2PPort;
        private string _masterSeed = string.Empty;
        private string _masterPeerId = string.Empty;

        private int _listenPort = 9822;
        private bool _isListening = false;
        private string _statusText = "服务已停止";

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 内置 P2P 中继服务视图模型 (Tab 3)
        /// </summary>
        public RelayServerViewModel RelayViewModel { get; } = new();

        public bool IsP2PMode
        {
            get => _isP2PMode;
            set
            {
                _isP2PMode = value;
                _isTcpMode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTcpMode));
            }
        }

        public bool IsTcpMode
        {
            get => _isTcpMode;
            set
            {
                _isTcpMode = value;
                _isP2PMode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsP2PMode));
            }
        }

        public string P2PServerAddress
        {
            get => _p2pServerAddress;
            set { _p2pServerAddress = value; OnPropertyChanged(); }
        }

        public int P2PPort
        {
            get => _p2pPort;
            set { _p2pPort = value; OnPropertyChanged(); }
        }

        public string MasterSeed
        {
            get => _masterSeed;
            set
            {
                _masterSeed = value;
                OnPropertyChanged();
            }
        }

        public string MasterPeerId
        {
            get => _masterPeerId;
            set
            {
                _masterPeerId = value;
                OnPropertyChanged();
            }
        }

        public int ListenPort
        {
            get => _listenPort;
            set { _listenPort = value; OnPropertyChanged(); }
        }

        public bool IsListening
        {
            get => _isListening;
            set
            {
                _isListening = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string ActionButtonText => IsListening ? "停止服务" : (IsP2PMode ? "启动 P2P 服务" : "启动监听 (Listen)");

        /// <summary>
        /// 当前接入的 Slave 设备动态集合
        /// </summary>
        public ObservableCollection<SlaveSession> SlaveDevices { get; } = new();

        /// <summary>
        /// 是否有已接入的 Slave 设备
        /// </summary>
        public bool HasSlaveDevices => SlaveDevices.Count > 0;

        /// <summary>
        /// 是否无任何 Slave 设备接入
        /// </summary>
        public bool HasNoSlaveDevices => SlaveDevices.Count == 0;

        public MasterViewModel()
        {
            // 加载或初始化持久化的 Master 身份
            var identity = MasterIdentityStorage.GetOrInitIdentity();
            _masterSeed = identity.IdentitySeed;
            _masterPeerId = identity.PeerId;
        }

        public void CopyMasterPeerId()
        {
            if (!string.IsNullOrWhiteSpace(MasterPeerId))
            {
                Clipboard.SetText(MasterPeerId);
                MessageBox.Show($"Master PeerId 已成功复制到剪贴板：\n{MasterPeerId}", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void SaveCustomSeed(string newSeed)
        {
            if (string.IsNullOrWhiteSpace(newSeed)) return;
            var updated = MasterIdentityStorage.SaveIdentity(newSeed.Trim());
            MasterSeed = updated.IdentitySeed;
            MasterPeerId = updated.PeerId;
        }

        public async Task ToggleListenAsync()
        {
            if (IsListening)
            {
                await StopAsync();
            }
            else
            {
                await StartAsync();
            }
        }

        public async Task StartAsync()
        {
            StatusText = IsP2PMode ? "正在启动 P2P 模式..." : $"正在启动端口 {ListenPort} 监听...";

            var options = new ScreenMasterOptions
            {
                ListenHost = "0.0.0.0",
                ListenPort = ListenPort,
                NodeName = $"Master-{Environment.MachineName}",
                UseP2P = IsP2PMode,
                P2PServerAddress = IsP2PMode && !string.IsNullOrWhiteSpace(P2PServerAddress) ? P2PServerAddress.Trim() : null,
                P2PPort = P2PPort,
                IdentitySeed = MasterSeed
            };

            _masterNode = new ScreenMasterNode(options);

            // 监听 Slave 设备动态变动
            _masterNode.SessionManager.SlaveConnected += (s, session) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (!SlaveDevices.Contains(session))
                    {
                        SlaveDevices.Add(session);
                        NotifyDevicesStateChanged();
                    }
                });
            };

            _masterNode.SessionManager.SlaveDisconnected += (s, session) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    SlaveDevices.Remove(session);
                    NotifyDevicesStateChanged();
                });
            };

            _masterNode.SessionManager.SlaveUpdated += (s, session) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    int index = SlaveDevices.IndexOf(session);
                    if (index >= 0)
                    {
                        SlaveDevices[index] = session;
                    }
                    NotifyDevicesStateChanged();
                });
            };

            bool started = await _masterNode.StartAsync();
            IsListening = started;
            if (started)
            {
                if (IsP2PMode)
                {
                    MasterPeerId = _masterNode.LocalPeerId ?? MasterPeerId;
                    StatusText = !string.IsNullOrWhiteSpace(P2PServerAddress)
                        ? $"已连入 P2P Relay: {P2PServerAddress} (等待 Slave 接入...)"
                        : $"本地 P2P 引导服务已启动 (端口: {P2PPort})，等待 Slave 接入...";
                }
                else
                {
                    StatusText = $"正在监听 0.0.0.0:{ListenPort} (等待 Slave 接入...)";
                }
            }
            else
            {
                StatusText = "服务启动失败，请检查网络配置或端口占用情况";
            }
        }

        public async Task StopAsync()
        {
            StatusText = "正在停止服务...";
            if (_masterNode != null)
            {
                await _masterNode.StopAsync();
                _masterNode.Dispose();
                _masterNode = null;
            }
            IsListening = false;
            SlaveDevices.Clear();
            NotifyDevicesStateChanged();
            StatusText = "服务已停止";
        }

        public void Stop()
        {
            Task.Run(async () => await StopAsync()).GetAwaiter().GetResult();
        }

        private void NotifyDevicesStateChanged()
        {
            OnPropertyChanged(nameof(HasSlaveDevices));
            OnPropertyChanged(nameof(HasNoSlaveDevices));
        }

        /// <summary>
        /// 打开选中的 Slave 设备的远程控制视窗
        /// </summary>
        public void OpenRemoteViewer(SlaveSession session)
        {
            if (session == null) return;
            var window = new RemoteViewerWindow(session);
            window.Show();
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
