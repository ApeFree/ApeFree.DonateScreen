using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using STTech.BytesIO.Tcp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// Slave 受控端节点管理器。
    /// 负责主动向 Master 建立 TCP 连接、挂载 ApeRpc 插件、发布屏幕与键鼠服务。
    /// </summary>
    public class ScreenSlaveNode : IDisposable
    {
        private readonly ScreenSlaveOptions _options;
        private readonly object _lock = new();
        private RpcTerminal? _terminal;
        private TcpClient? _tcpClient;
        private BytesRpcTerminalAddon? _addon;
        private ScreenShareService? _screenService;
        private InputControlService? _inputService;
        private CancellationTokenSource? _reconnectCts;
        private bool _isDisposed;

        /// <summary>
        /// 连接状态发生变化事件
        /// </summary>
        public event EventHandler<bool>? ConnectionStatusChanged;

        /// <summary>
        /// 是否处于已连接状态
        /// </summary>
        public bool IsConnected => _tcpClient != null && _tcpClient.IsConnected;

        /// <summary>
        /// 屏幕共享核心服务
        /// </summary>
        public ScreenShareService? ScreenService => _screenService;

        /// <summary>
        /// 键盘鼠标控制服务（在仅观看模式下为 null）
        /// </summary>
        public InputControlService? InputService => _inputService;

        /// <summary>
        /// 节点配置
        /// </summary>
        public ScreenSlaveOptions Options => _options;

        public ScreenSlaveNode(ScreenSlaveOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 启动连接到 Master 端
        /// </summary>
        public async Task<bool> StartAsync()
        {
            lock (_lock)
            {
                if (IsConnected) return true;
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
            }

            return await ConnectInternalAsync();
        }

        private async Task<bool> ConnectInternalAsync()
        {
            try
            {
                // 1. 初始化终端
                string nodeName = string.IsNullOrWhiteSpace(_options.NodeName)
                    ? $"Slave-{Environment.MachineName}"
                    : _options.NodeName;

                _terminal = new RpcTerminal(new TerminalInfo { Name = nodeName });

                // 2. 创建通信客户端
                _tcpClient = new TcpClient
                {
                    Host = _options.MasterHost,
                    Port = _options.MasterPort
                };

                // 3. 挂载 BytesRpc 插件
                _addon = new BytesRpcTerminalAddon(_terminal, _tcpClient);

                // 4. 注册本地服务
                _screenService = new ScreenShareService(_options.CaptureOptions, null, _options.ShareMode, nodeName);
                _terminal.RegisterService(_screenService);

                if (_options.ShareMode == ScreenShareMode.FullControl)
                {
                    _inputService = new InputControlService();
                    _terminal.RegisterService(_inputService);
                }
                else
                {
                    _inputService = null;
                }

                // 5. 监听断线与重连
                _tcpClient.OnDisconnected += OnDisconnected;

                // 6. 发起连接
                var connectResult = await _tcpClient.ConnectAsync();
                if (!connectResult.IsSuccess)
                {
                    ScheduleReconnect();
                    return false;
                }

                // 7. 启动屏幕抓取推流循环
                _screenService.Start();

                // 8. 向上线登记 Master 节点
                await NotifyMasterOnlineAsync();

                ConnectionStatusChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception)
            {
                ScheduleReconnect();
                return false;
            }
        }

        private async Task NotifyMasterOnlineAsync()
        {
            try
            {
                if (_terminal == null) return;
                var masterService = _terminal.GetService<IMasterScreenService>(nameof(MasterScreenService));
                if (masterService != null)
                {
                    var topo = _screenService?.ScreenTopology;
                    masterService.RegisterSlaveNode(new SlaveNodeRegistrationDto
                    {
                        SlaveId = _options.NodeName,
                        MachineName = Environment.MachineName,
                        OsVersion = Environment.OSVersion.ToString(),
                        ShareMode = _options.ShareMode,
                        DisplayWidth = topo?.Width ?? 1920,
                        DisplayHeight = topo?.Height ?? 1080,
                        Note = _options.NodeName
                    });
                }
            }
            catch
            {
                // 若 Master 尚未注册协同服务，不影响主要屏幕推流
            }
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            _screenService?.Stop();
            _inputService?.ResetInputState();

            ConnectionStatusChanged?.Invoke(this, false);

            if (!_isDisposed && _options.AutoReconnect)
            {
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_isDisposed || !_options.AutoReconnect) return;

            var token = _reconnectCts?.Token ?? CancellationToken.None;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_options.ReconnectIntervalMs, token);
                    if (!token.IsCancellationRequested && !_isDisposed)
                    {
                        await ConnectInternalAsync();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        /// <summary>
        /// 断开连接并释放资源
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _reconnectCts?.Cancel();
                _screenService?.Stop();
                _inputService?.ResetInputState();

                if (_tcpClient != null)
                {
                    _tcpClient.OnDisconnected -= OnDisconnected;
                    _tcpClient.Disconnect();
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }

                _addon?.Dispose();
                _addon = null;
                _terminal = null;
            }

            ConnectionStatusChanged?.Invoke(this, false);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            _screenService?.Dispose();
        }
    }
}
