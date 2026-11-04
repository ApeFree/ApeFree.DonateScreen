using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Slave;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ApeFree.DonateScreen.WinSlaveDesktop.ViewModels
{
    public class SlaveShareViewModel : INotifyPropertyChanged
    {
        private ScreenSlaveNode? _slaveNode;
        private DispatcherTimer? _timer;

        private bool _isP2PMode = true;
        private bool _isTcpMode = false;
        private string _p2pServerAddress = "127.0.0.1:9832";
        private string _targetMasterPeerId = string.Empty;
        private string _identitySeed = string.Empty;

        private string _masterHost = "127.0.0.1";
        private int _masterPort = 9822;
        private int _listenPort = 9822;
        private bool _isConnectMode = true;
        private bool _isListenMode = false;
        private string _nodeName = Environment.MachineName;
        private bool _isFullControl = true;
        private bool _isViewOnly = false;
        private int _maxFps = 30;
        private int _crf = 25;
        private int _jpegQuality = 75;
        private VideoCodecType _selectedCodec = VideoCodecType.H264;
        private PixelFormatMode _selectedPixelFormat = PixelFormatMode.Rgb24;

        private bool _isConnected = false;
        private string _statusText = "就绪 (未连接)";
        private int _currentFps = 0;
        private string _currentBitrate = "0 KB/s";
        private string _engineName = "就绪";

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsP2PMode
        {
            get => _isP2PMode;
            set
            {
                _isP2PMode = value;
                _isTcpMode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTcpMode));
                OnPropertyChanged(nameof(ActionButtonText));
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
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string P2PServerAddress
        {
            get => _p2pServerAddress;
            set { _p2pServerAddress = value; OnPropertyChanged(); }
        }

        public string TargetMasterPeerId
        {
            get => _targetMasterPeerId;
            set { _targetMasterPeerId = value; OnPropertyChanged(); }
        }

        public string IdentitySeed
        {
            get => _identitySeed;
            set { _identitySeed = value; OnPropertyChanged(); }
        }

        public bool IsConnectMode
        {
            get => _isConnectMode;
            set
            {
                _isConnectMode = value;
                _isListenMode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListenMode));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public bool IsListenMode
        {
            get => _isListenMode;
            set
            {
                _isListenMode = value;
                _isConnectMode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnectMode));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string MasterHost
        {
            get => _masterHost;
            set { _masterHost = value; OnPropertyChanged(); }
        }

        public int MasterPort
        {
            get => _masterPort;
            set { _masterPort = value; OnPropertyChanged(); }
        }

        public int ListenPort
        {
            get => _listenPort;
            set { _listenPort = value; OnPropertyChanged(); }
        }

        public string NodeName
        {
            get => _nodeName;
            set { _nodeName = value; OnPropertyChanged(); }
        }

        public bool IsFullControl
        {
            get => _isFullControl;
            set
            {
                _isFullControl = value;
                _isViewOnly = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsViewOnly));
            }
        }

        public bool IsViewOnly
        {
            get => _isViewOnly;
            set
            {
                _isViewOnly = value;
                _isFullControl = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFullControl));
            }
        }

        public int MaxFps
        {
            get => _maxFps;
            set
            {
                _maxFps = value;
                OnPropertyChanged();
                UpdateOptions();
            }
        }

        public int Crf
        {
            get => _crf;
            set
            {
                _crf = value;
                OnPropertyChanged();
                UpdateOptions();
            }
        }

        public int JpegQuality
        {
            get => _jpegQuality;
            set
            {
                _jpegQuality = value;
                OnPropertyChanged();
                UpdateOptions();
            }
        }

        public VideoCodecType SelectedCodec
        {
            get => _selectedCodec;
            set
            {
                _selectedCodec = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCodecIndex));
                OnPropertyChanged(nameof(IsH264));
                OnPropertyChanged(nameof(IsJpeg));
                UpdateOptions();
            }
        }

        public bool IsH264 => SelectedCodec == VideoCodecType.H264;
        public bool IsJpeg => SelectedCodec == VideoCodecType.Jpeg;

        public int SelectedCodecIndex
        {
            get => (int)_selectedCodec;
            set
            {
                SelectedCodec = (VideoCodecType)Math.Clamp(value, 0, 1);
            }
        }

        public PixelFormatMode SelectedPixelFormat
        {
            get => _selectedPixelFormat;
            set
            {
                _selectedPixelFormat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedPixelFormatIndex));
                UpdateOptions();
            }
        }

        public int SelectedPixelFormatIndex
        {
            get => (int)_selectedPixelFormat;
            set
            {
                SelectedPixelFormat = (PixelFormatMode)Math.Clamp(value, 0, 2);
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public int CurrentFps
        {
            get => _currentFps;
            set { _currentFps = value; OnPropertyChanged(); }
        }

        public string CurrentBitrate
        {
            get => _currentBitrate;
            set { _currentBitrate = value; OnPropertyChanged(); }
        }

        public string EngineName
        {
            get => _engineName;
            set { _engineName = value; OnPropertyChanged(); }
        }

        public string ActionButtonText
        {
            get
            {
                if (IsConnected)
                {
                    return (IsTcpMode && IsListenMode) ? "停止监听" : "断开分享";
                }
                return (IsTcpMode && IsListenMode) ? "启动受控服务 (Listen)" : "开始分享 (Connect)";
            }
        }

        public SlaveShareViewModel()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => UpdateLiveStats();
            _timer.Start();
        }

        public async Task ToggleConnectAsync()
        {
            if (IsConnected)
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
            if (IsP2PMode && string.IsNullOrWhiteSpace(TargetMasterPeerId))
            {
                StatusText = "错误：P2P 模式必须输入目标 Master PeerId";
                return;
            }

            if (IsP2PMode)
            {
                StatusText = "正在通过 P2P 连接 Master...";
            }
            else
            {
                StatusText = IsListenMode ? $"正在开放端口 {ListenPort} 监听..." : "正在连接 Master...";
            }

            var options = new ScreenSlaveOptions
            {
                UseP2P = IsP2PMode,
                P2PServerAddress = IsP2PMode ? P2PServerAddress.Trim() : null,
                TargetMasterPeerId = IsP2PMode ? TargetMasterPeerId.Trim() : null,
                IdentitySeed = !string.IsNullOrWhiteSpace(IdentitySeed) ? IdentitySeed.Trim() : null,
                TransportMode = IsListenMode ? NetworkTransportMode.Listen : NetworkTransportMode.Connect,
                MasterHost = MasterHost.Trim(),
                MasterPort = MasterPort,
                ListenHost = "0.0.0.0",
                ListenPort = ListenPort,
                NodeName = NodeName.Trim(),
                ShareMode = IsFullControl ? ScreenShareMode.FullControl : ScreenShareMode.ViewOnly,
                CaptureOptions = new CaptureOptionsDto
                {
                    MaxFps = MaxFps,
                    Crf = Crf,
                    JpegQuality = JpegQuality,
                    PixelFormat = SelectedPixelFormat,
                    Codec = SelectedCodec
                },
                AutoReconnect = true
            };

            _slaveNode = new ScreenSlaveNode(options);
            _slaveNode.ConnectionStatusChanged += (s, connected) =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsConnected = connected;
                    if (IsTcpMode && IsListenMode)
                    {
                        StatusText = _slaveNode.ConnectedMasterCount > 0
                            ? $"受控中 (当前接入 {_slaveNode.ConnectedMasterCount} 个 Master)"
                            : $"监听就绪 (端口 {ListenPort}，等待 Master 连入...)";
                    }
                    else
                    {
                        StatusText = connected ? "已连接受控中" : "已断开连接 (重连中...)";
                    }
                });
            };

            bool success = await _slaveNode.StartAsync();
            IsConnected = success;
            if (success)
            {
                if (IsP2PMode)
                {
                    StatusText = $"已通过 P2P 成功接入 Master ({TargetMasterPeerId[..Math.Min(8, TargetMasterPeerId.Length)]}...)";
                }
                else if (IsListenMode)
                {
                    StatusText = $"服务端监听已就绪 (端口 {ListenPort})，等待 Master 连入...";
                }
                else
                {
                    StatusText = $"已成功连接至 {MasterHost}:{MasterPort}";
                }
                EngineName = _slaveNode.ScreenService?.EngineName ?? "FFmpeg H.264 / DirectX";
            }
            else
            {
                if (IsP2PMode)
                {
                    StatusText = "连接建立中，后台持续尝试中...";
                }
                else if (IsListenMode)
                {
                    StatusText = "启动服务端监听失败，请检查端口是否被占用";
                }
                else
                {
                    StatusText = "连接失败，正在后台持续重连...";
                }
            }
        }

        public async Task StopAsync()
        {
            StatusText = "正在断开分享...";
            if (_slaveNode != null)
            {
                await Task.Run(() =>
                {
                    _slaveNode.Stop();
                    _slaveNode.Dispose();
                }).ConfigureAwait(false);
                _slaveNode = null;
            }
            IsConnected = false;
            StatusText = "已断开分享";
            CurrentFps = 0;
            CurrentBitrate = "0 KB/s";
        }

        public void Stop()
        {
            Task.Run(async () => await StopAsync()).GetAwaiter().GetResult();
        }

        public void EmergencyCutoff()
        {
            _slaveNode?.InputService?.ResetInputState();
            if (_slaveNode?.InputService != null)
            {
                _slaveNode.InputService.IsEnabled = false;
            }
            Stop();
            StatusText = "已触发紧急切断，本地控制已完全收回！";
        }

        public void RequestKeyFrame()
        {
            _slaveNode?.ScreenService?.RequestKeyFrame();
        }

        private void UpdateOptions()
        {
            if (_slaveNode?.ScreenService != null)
            {
                _slaveNode.ScreenService.UpdateCaptureOptions(new CaptureOptionsDto
                {
                    MaxFps = MaxFps,
                    Crf = Crf,
                    JpegQuality = JpegQuality,
                    PixelFormat = SelectedPixelFormat,
                    Codec = SelectedCodec
                });
            }
        }

        private void UpdateLiveStats()
        {
            if (_slaveNode?.ScreenService != null && IsConnected)
            {
                CurrentFps = _slaveNode.ScreenService.CurrentFps;
                CurrentBitrate = $"{_slaveNode.ScreenService.CurrentBitrateKbps:F1} KB/s";
                EngineName = _slaveNode.ScreenService.EngineName;

                if (IsTcpMode && IsListenMode)
                {
                    StatusText = _slaveNode.ConnectedMasterCount > 0
                        ? $"受控中 (当前接入 {_slaveNode.ConnectedMasterCount} 个 Master 客户端)"
                        : $"监听端口 {ListenPort} 运行中 (等待 Master 接入...)";
                }
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
