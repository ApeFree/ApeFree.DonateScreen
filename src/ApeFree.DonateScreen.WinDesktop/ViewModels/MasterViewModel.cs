using ApeFree.DonateScreen.Master;
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
        private int _listenPort = 9822;
        private bool _isListening = false;
        private string _statusText = "服务已停止";

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public string ActionButtonText => IsListening ? "停止监听" : "启动监听 (Listen)";

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
            StatusText = $"正在启动端口 {ListenPort} 监听...";

            var options = new ScreenMasterOptions
            {
                ListenHost = "0.0.0.0",
                ListenPort = ListenPort,
                NodeName = $"Master-{Environment.MachineName}"
            };

            _masterNode = new ScreenMasterNode(options);

            // 监听 Slave 设备动态变动 (采用非阻塞 InvokeAsync，彻底避免主从跨线程死锁)
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
            StatusText = started
                ? $"正在监听 0.0.0.0:{ListenPort} (等待 Slave 接入...)"
                : "启动监听失败，请检查端口是否被占用";
        }

        public async Task StopAsync()
        {
            StatusText = "正在停止监听服务...";
            if (_masterNode != null)
            {
                await _masterNode.StopAsync();
                _masterNode.Dispose();
                _masterNode = null;
            }
            IsListening = false;
            SlaveDevices.Clear();
            NotifyDevicesStateChanged();
            StatusText = "监听服务已停止";
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
