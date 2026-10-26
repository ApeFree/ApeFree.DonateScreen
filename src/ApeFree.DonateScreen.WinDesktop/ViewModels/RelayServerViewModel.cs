using ApeFree.DonateScreen.P2P;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace ApeFree.DonateScreen.WinDesktop.ViewModels
{
    /// <summary>
    /// P2P 中继服务端 (Relay Server) 视图模型
    /// </summary>
    public class RelayServerViewModel : INotifyPropertyChanged, IDisposable
    {
        private ScreenRelayServer? _relayServer;
        private int _port = P2PAddressHelper.DefaultP2PPort;
        private string _host = "0.0.0.0";
        private string _identitySeed = string.Empty;
        private bool _isRunning = false;
        private int _activePeerCount = 0;
        private int _pairedSessionCount = 0;
        private string _statusText = "中继服务已停止";
        private string _localPeerId = "未启动";

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public string Host
        {
            get => _host;
            set { _host = value; OnPropertyChanged(); }
        }

        public string IdentitySeed
        {
            get => _identitySeed;
            set { _identitySeed = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public int ActivePeerCount
        {
            get => _activePeerCount;
            set { _activePeerCount = value; OnPropertyChanged(); }
        }

        public int PairedSessionCount
        {
            get => _pairedSessionCount;
            set { _pairedSessionCount = value; OnPropertyChanged(); }
        }

        public string LocalPeerId
        {
            get => _localPeerId;
            set { _localPeerId = value; OnPropertyChanged(); }
        }

        public string ActionButtonText => IsRunning ? "停止中继服务" : "启动中继服务 (Start)";

        public ObservableCollection<string> Logs { get; } = new();

        public RelayServerViewModel()
        {
            var defaultPeerId = P2PAddressHelper.GetPeerIdFromSeed(P2PAddressHelper.DefaultServerSeed);
            if (!string.IsNullOrWhiteSpace(defaultPeerId))
            {
                LocalPeerId = defaultPeerId;
            }
        }

        public async Task ToggleServerAsync()
        {
            if (IsRunning)
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
            StatusText = $"正在启动中继端口 {Port} 监听...";

            _relayServer = new ScreenRelayServer
            {
                Host = Host.Trim(),
                Port = Port,
                IdentitySeed = !string.IsNullOrWhiteSpace(IdentitySeed) ? IdentitySeed.Trim() : null
            };

            _relayServer.StatusChanged += (s, running) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsRunning = running;
                    StatusText = running ? $"正在监听 {Host}:{Port} (中继服务运行中)" : "中继服务已停止";
                    LocalPeerId = _relayServer?.LocalPeerId ?? "未知";
                });
            };

            _relayServer.PeerCountChanged += (s, count) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ActivePeerCount = count;
                    PairedSessionCount = _relayServer?.PairedSessionCount ?? 0;
                });
            };

            _relayServer.LogMessage += (s, msg) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Logs.Insert(0, msg);
                    while (Logs.Count > 200)
                    {
                        Logs.RemoveAt(Logs.Count - 1);
                    }
                });
            };

            bool started = await _relayServer.StartAsync();
            IsRunning = started;
            if (started)
            {
                LocalPeerId = _relayServer.LocalPeerId ?? "未知";
                StatusText = $"正在监听 {Host}:{Port} (中继服务运行中)";
            }
            else
            {
                StatusText = "启动中继服务失败，请检查端口是否被占用";
            }
        }

        public async Task StopAsync()
        {
            StatusText = "正在停止中继服务...";
            if (_relayServer != null)
            {
                await Task.Run(() =>
                {
                    _relayServer.Stop();
                    _relayServer.Dispose();
                }).ConfigureAwait(false);
                _relayServer = null;
            }

            IsRunning = false;
            ActivePeerCount = 0;
            PairedSessionCount = 0;
            StatusText = "中继服务已停止";
        }

        public void ClearLogs()
        {
            Logs.Clear();
        }

        public void Dispose()
        {
            _relayServer?.Dispose();
            _relayServer = null;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
