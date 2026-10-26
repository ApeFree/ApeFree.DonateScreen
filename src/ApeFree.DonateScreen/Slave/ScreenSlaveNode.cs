using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.P2P;
using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using STTech.BytesIO.Tcp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// Slave 受控端节点管理器。
    /// 负责主动向 Master 建立 TCP 连接或 P2P (Libp2p) 去中心化中继、挂载 ApeRpc 插件、发布屏幕与键鼠服务。
    /// </summary>
    public class ScreenSlaveNode : IDisposable
    {
        private readonly ScreenSlaveOptions _options;
        private readonly object _lock = new();
        private RpcTerminal? _terminal;
        private BytesClient? _client;
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
        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>
        /// 若处于 P2P 模式，获取当前本地节点的 PeerId
        /// </summary>
        public string? LocalPeerId => (_client as PeerClient)?.LocalPeerId ?? P2PAddressHelper.GetPeerIdFromSeed(_options.IdentitySeed);

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

                // 2. 创建通信客户端 (P2P 或标准 TCP)
                if (_options.UseP2P)
                {
                    var peerClient = new PeerClient();
                    peerClient.Options.EnableRelay = _options.EnableRelay;
                    peerClient.Options.EnableDht = _options.EnableDht;
                    peerClient.Options.EnableMdns = _options.EnableMdns;
                    peerClient.Options.EnableQuic = _options.EnableQuic;

                    if (!string.IsNullOrWhiteSpace(_options.IdentitySeed))
                    {
                        peerClient.Options.IdentitySeed = P2PAddressHelper.ParseSeed(_options.IdentitySeed);
                    }

                    if (!string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                    {
                        peerClient.RemoteAddress = P2PAddressHelper.ResolveServerMultiaddr(_options.P2PServerAddress, _options.ServerPeerId, _options.ServerSeed);
                    }
                    else if (!string.IsNullOrWhiteSpace(_options.TargetMasterPeerId))
                    {
                        peerClient.RemoteAddress = P2PAddressHelper.ToMultiaddr($"{_options.MasterHost}:{_options.MasterPort}", _options.TargetMasterPeerId);
                    }
                    else
                    {
                        peerClient.RemoteAddress = P2PAddressHelper.ToMultiaddr($"{_options.MasterHost}:{_options.MasterPort}");
                    }

                    _client = peerClient;
                }
                else
                {
                    _client = new TcpClient
                    {
                        Host = _options.MasterHost,
                        Port = _options.MasterPort
                    };
                }

                // 3. 挂载 BytesRpc 插件
                _addon = new BytesRpcTerminalAddon(_terminal, _client);

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
                _client.OnDisconnected += OnDisconnected;

                // 6. 发起连接
                var connectResult = await _client.ConnectAsync(new ConnectArgument { Timeout = 10000 });
                if (!connectResult.IsSuccess)
                {
                    ScheduleReconnect();
                    return false;
                }

                // 若在 P2P 模式下，向服务端发送会话配对握手报文
                if (_options.UseP2P && _client is PeerClient p2p)
                {
                    var handshake = P2PRelayManager.CreateHandshakePacket("Slave", p2p.LocalPeerId, _options.TargetMasterPeerId);
                    p2p.Send(handshake);
                }

                // 7. 启动屏幕抓取推流循环
                _screenService.Start();

                // 8. 通知 Master 上线注册元数据（异步带重试）
                NotifyMasterOnlineWithRetry();

                ConnectionStatusChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception)
            {
                ScheduleReconnect();
                return false;
            }
        }

        private void NotifyMasterOnlineWithRetry()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                for (int i = 0; i < 10; i++)
                {
                    if (_isDisposed || _client == null || !_client.IsConnected) break;
                    try
                    {
                        var masterService = _addon?.QueryService<IMasterScreenService>(nameof(MasterScreenService), _client);
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
                                Note = _options.NodeName,
                                TargetMasterPeerId = _options.TargetMasterPeerId,
                                LocalPeerId = LocalPeerId
                            });
                            break;
                        }
                    }
                    catch
                    {
                        await Task.Delay(400).ConfigureAwait(false);
                    }
                }
            });
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

                if (_client != null)
                {
                    _client.OnDisconnected -= OnDisconnected;
                    _client.Disconnect();
                    _client.Dispose();
                    _client = null;
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
