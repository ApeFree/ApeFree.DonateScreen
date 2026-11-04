using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.P2P;
using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using STTech.BytesIO.Tcp;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// Slave 受控端节点管理器。
    /// 负责主动向 Master 建立 TCP/P2P 连接（反向穿透/中继模式）或开放本地端口监听等待 Master 接入（正向受控服务模式），
    /// 挂载 ApeRpc 插件、发布屏幕与键鼠服务。
    /// </summary>
    public class ScreenSlaveNode : IDisposable
    {
        private readonly ScreenSlaveOptions _options;
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<BytesClient, bool> _connectedMasters = new();
        private RpcTerminal? _terminal;
        private BytesClient? _client;
        private TcpServer? _tcpServer;
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
        /// 当有 Master 客户端接入时触发 (Listen 模式)
        /// </summary>
        public event EventHandler<BytesClient>? MasterConnected;

        /// <summary>
        /// 当 Master 客户端断开连接时触发 (Listen 模式)
        /// </summary>
        public event EventHandler<BytesClient>? MasterDisconnected;

        /// <summary>
        /// 是否处于已连接状态（Connect 模式下已连上 Master，或 Listen 模式下有至少一个 Master 接入）
        /// </summary>
        public bool IsConnected => (_client != null && _client.IsConnected) || !_connectedMasters.IsEmpty;

        /// <summary>
        /// 本地服务端是否正在运行监听 (Listen 模式)
        /// </summary>
        public bool IsListening => _tcpServer != null && _tcpServer.IsListening;

        /// <summary>
        /// 当前已连入的 Master 客户端数量
        /// </summary>
        public int ConnectedMasterCount => _connectedMasters.Count;

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
        /// 启动 Slave 节点（按配置选择监听端口或主动连接 Master）
        /// </summary>
        public async Task<bool> StartAsync()
        {
            lock (_lock)
            {
                if (IsConnected || IsListening) return true;
                _reconnectCts?.Cancel();
                _reconnectCts = new CancellationTokenSource();
            }

            if (!_options.UseP2P && _options.TransportMode == NetworkTransportMode.Listen)
            {
                return await StartListeningAsync();
            }
            else
            {
                return await ConnectInternalAsync();
            }
        }

        #region 正向受控服务 (Listen 模式)

        private async Task<bool> StartListeningAsync()
        {
            try
            {
                string nodeName = string.IsNullOrWhiteSpace(_options.NodeName)
                    ? $"Slave-{Environment.MachineName}"
                    : _options.NodeName;

                _terminal = new RpcTerminal(new TerminalInfo { Name = nodeName });
                _addon = new BytesRpcTerminalAddon(_terminal);

                _tcpServer = new TcpServer
                {
                    Host = _options.ListenHost,
                    Port = _options.ListenPort
                };

                _addon.BindServer(_tcpServer);

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

                _tcpServer.ClientConnected += (s, e) =>
                {
                    _connectedMasters.TryAdd(e.Client, true);
                    MasterConnected?.Invoke(this, e.Client);
                    ConnectionStatusChanged?.Invoke(this, true);
                };

                _tcpServer.ClientDisconnected += (s, e) =>
                {
                    _connectedMasters.TryRemove(e.Client, out _);
                    MasterDisconnected?.Invoke(this, e.Client);
                    if (_connectedMasters.IsEmpty)
                    {
                        _inputService?.ResetInputState();
                        ConnectionStatusChanged?.Invoke(this, false);
                    }
                };

                await _tcpServer.StartAsync().ConfigureAwait(false);
                _screenService.Start();

                ConnectionStatusChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 反向穿透连出 (Connect 模式及 P2P 模式)

        private async Task<bool> ConnectInternalAsync()
        {
            try
            {
                string nodeName = string.IsNullOrWhiteSpace(_options.NodeName)
                    ? $"Slave-{Environment.MachineName}"
                    : _options.NodeName;

                _terminal = new RpcTerminal(new TerminalInfo { Name = nodeName });
                _addon = new BytesRpcTerminalAddon(_terminal);

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

                _addon.BindClient(_client);

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

                _client.OnDisconnected += OnDisconnected;

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

                _screenService.Start();
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

            if (!_isDisposed && _options.AutoReconnect && (_options.UseP2P || _options.TransportMode == NetworkTransportMode.Connect))
            {
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_isDisposed || !_options.AutoReconnect) return;
            if (!_options.UseP2P && _options.TransportMode != NetworkTransportMode.Connect) return;

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

        #endregion

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

                if (_tcpServer != null)
                {
                    try
                    {
                        _tcpServer.CloseAsync().GetAwaiter().GetResult();
                    }
                    catch { }
                    _tcpServer.Dispose();
                    _tcpServer = null;
                }

                if (_client != null)
                {
                    _client.OnDisconnected -= OnDisconnected;
                    _client.Disconnect();
                    _client.Dispose();
                    _client = null;
                }

                _connectedMasters.Clear();

                _addon?.Dispose();
                _addon = null;
                _terminal = null;
            }

            ConnectionStatusChanged?.Invoke(this, false);
        }

        public async Task StopAsync()
        {
            TcpServer? serverToClose = null;
            BytesClient? clientToClose = null;

            lock (_lock)
            {
                _reconnectCts?.Cancel();
                _screenService?.Stop();
                _inputService?.ResetInputState();

                if (_tcpServer != null)
                {
                    serverToClose = _tcpServer;
                    _tcpServer = null;
                }

                if (_client != null)
                {
                    clientToClose = _client;
                    _client = null;
                }

                _connectedMasters.Clear();

                _addon?.Dispose();
                _addon = null;
                _terminal = null;
            }

            if (serverToClose != null)
            {
                try
                {
                    await serverToClose.CloseAsync().ConfigureAwait(false);
                }
                catch { }
                serverToClose.Dispose();
            }

            if (clientToClose != null)
            {
                clientToClose.OnDisconnected -= OnDisconnected;
                clientToClose.Disconnect();
                clientToClose.Dispose();
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
