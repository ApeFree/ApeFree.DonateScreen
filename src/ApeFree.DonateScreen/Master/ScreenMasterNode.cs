using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.P2P;
using ApeFree.DonateScreen.Slave;
using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using STTech.BytesIO.Tcp;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 主控端服务监听与控制节点。
    /// 负责开启 TCP 监听或 P2P 中继等待受控端连入（反向模式），或主动向指定的受控端发起 TCP 连接（正向模式），
    /// 管理连入的 Slave 客户端并提供动态服务代理。
    /// </summary>
    public class ScreenMasterNode : IDisposable
    {
        private readonly ScreenMasterOptions _options;
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, TcpClient> _outboundClients = new();
        private RpcTerminal? _terminal;
        private TcpServer? _tcpServer;
        private TcpClient? _tcpClient;
        private BootstrapServer? _bootstrapServer;
        private PeerClient? _p2pClient;
        private BytesRpcTerminalAddon? _addon;
        private MasterScreenService? _masterService;
        private readonly SlaveSessionManager _sessionManager = new();
        private CancellationTokenSource? _initialConnectCts;
        private CancellationTokenSource? _p2pWatcherCts;
        private bool _isDisposed;

        /// <summary>
        /// Slave 会话管理器
        /// </summary>
        public SlaveSessionManager SessionManager => _sessionManager;

        /// <summary>
        /// 是否正在运行（Listen 模式下为监听状态，Connect 模式下为客户端连接状态）
        /// </summary>
        public bool IsRunning => (_tcpServer != null && _tcpServer.IsListening) ||
                                 (_tcpClient != null && _tcpClient.IsConnected) ||
                                 (_bootstrapServer != null && _bootstrapServer.IsListening) ||
                                 (_p2pClient != null && _p2pClient.IsConnected);

        /// <summary>
        /// 获取当前 Master 本地节点的 PeerId
        /// </summary>
        public string? LocalPeerId => _p2pClient?.LocalPeerId ??
                                      _bootstrapServer?.LocalPeerId ??
                                      P2PAddressHelper.GetPeerIdFromSeed(_options.IdentitySeed);

        /// <summary>
        /// 配置参数
        /// </summary>
        public ScreenMasterOptions Options => _options;

        public ScreenMasterNode(ScreenMasterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 启动 Master 节点（按配置选择开放本地监听、主动直连目标 Slave 或启动 P2P 去中心化连接）
        /// </summary>
        public async Task<bool> StartAsync()
        {
            lock (_lock)
            {
                if (IsRunning) return true;

                // 1. 初始化终端与 RPC 插件
                _terminal = new RpcTerminal(new TerminalInfo { Name = _options.NodeName });
                _addon = new BytesRpcTerminalAddon(_terminal);

                // 2. 注册协同服务
                _masterService = new MasterScreenService();
                _masterService.SlaveRegistered += OnSlaveReportedRegistration;
                _terminal.RegisterService(_masterService);
            }

            if (_options.UseP2P)
            {
                return await StartP2PAsync().ConfigureAwait(false);
            }
            else if (_options.TransportMode == NetworkTransportMode.Connect)
            {
                return await StartConnectAsync().ConfigureAwait(false);
            }
            else
            {
                return await StartListeningAsync().ConfigureAwait(false);
            }
        }

        #region P2P 模式

        private async Task<bool> StartP2PAsync()
        {
            if (!string.IsNullOrWhiteSpace(_options.P2PServerAddress))
            {
                // 连入公共 P2P Relay 服务端
                var peerClient = new PeerClient();
                peerClient.RemoteAddress = P2PAddressHelper.ResolveServerMultiaddr(_options.P2PServerAddress, _options.ServerPeerId, _options.ServerSeed);
                peerClient.Options.EnableRelay = _options.EnableRelay;
                peerClient.Options.EnableDht = _options.EnableDht;
                peerClient.Options.EnableMdns = _options.EnableMdns;
                peerClient.Options.EnableQuic = _options.EnableQuic;

                if (!string.IsNullOrWhiteSpace(_options.IdentitySeed))
                {
                    peerClient.Options.IdentitySeed = P2PAddressHelper.ParseSeed(_options.IdentitySeed);
                }

                _p2pClient = peerClient;
                _addon!.BindClient(peerClient);

                // 开启无限自动重连
                peerClient.UseAutoReconnect(3000, 0);

                peerClient.OnConnectedSuccessfully += (s, e) =>
                {
                    _initialConnectCts?.Cancel();

                    // 发送 Master 角色注册握手包给 Relay Server
                    var handshake = P2PRelayManager.CreateHandshakePacket("Master", peerClient.LocalPeerId, null);
                    peerClient.Send(handshake);

                    StartP2PSlaveWatcher(peerClient);
                };

                peerClient.OnDisconnected += (s, e) =>
                {
                    _p2pWatcherCts?.Cancel();
                    HandleClientDisconnected(peerClient);
                };

                var result = await _p2pClient.ConnectAsync(new ConnectArgument { Timeout = 10000 }).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    StartInitialConnectLoop(_p2pClient);
                }
                else
                {
                    StartP2PSlaveWatcher(_p2pClient);
                }
                return true;
            }
            else
            {
                // 本地开启 P2P 监听服务 (BootstrapServer)
                _bootstrapServer = new BootstrapServer();
                _bootstrapServer.Options.ListenAddress = _options.ListenHost;
                _bootstrapServer.Options.TcpPort = _options.P2PPort > 0 ? _options.P2PPort : P2PAddressHelper.DefaultP2PPort;
                _bootstrapServer.Options.EnableRelay = _options.EnableRelay;
                _bootstrapServer.Options.EnableDht = _options.EnableDht;
                _bootstrapServer.Options.EnableMdns = _options.EnableMdns;
                _bootstrapServer.Options.EnableQuic = _options.EnableQuic;

                if (!string.IsNullOrWhiteSpace(_options.IdentitySeed))
                {
                    _bootstrapServer.Options.IdentitySeed = P2PAddressHelper.ParseSeed(_options.IdentitySeed);
                }

                _bootstrapServer.PeerConnected += (s, e) =>
                {
                    _addon!.BindClient(e.Client);
                    HandleClientConnected(e.Client);
                };

                _bootstrapServer.PeerDisconnected += (s, e) =>
                {
                    _addon!.UnbindClient(e.Client);
                    HandleClientDisconnected(e.Client);
                };

                await _bootstrapServer.StartAsync().ConfigureAwait(false);
                return true;
            }
        }

        private void StartInitialConnectLoop(PeerClient client)
        {
            _initialConnectCts?.Cancel();
            _initialConnectCts = new CancellationTokenSource();
            var token = _initialConnectCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed && client != null && !client.IsConnected)
                {
                    try
                    {
                        await Task.Delay(3000, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _isDisposed || client == null || client.IsConnected) break;
                        await client.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = token }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        private void StartP2PSlaveWatcher(PeerClient client)
        {
            _p2pWatcherCts?.Cancel();
            _p2pWatcherCts = new CancellationTokenSource();
            var token = _p2pWatcherCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && client.IsConnected && !_isDisposed)
                {
                    if (!_sessionManager.Sessions.Any())
                    {
                        try
                        {
                            var screenProxy = _addon?.QueryService<IScreenShareService>(nameof(ScreenShareService), client);
                            if (screenProxy != null)
                            {
                                var regInfo = screenProxy.GetRegistrationInfo();
                                if (regInfo != null)
                                {
                                    OnSlaveReportedRegistration(this, regInfo);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(400, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }, token);
        }

        #endregion

        #region 服务端监听 (Listen 模式)

        private async Task<bool> StartListeningAsync()
        {
            lock (_lock)
            {
                _tcpServer = new TcpServer
                {
                    Host = _options.ListenHost,
                    Port = _options.ListenPort
                };

                _addon!.BindServer(_tcpServer);

                _tcpServer.ClientConnected += (s, e) => HandleClientConnected(e.Client);
                _tcpServer.ClientDisconnected += (s, e) => HandleClientDisconnected(e.Client);
            }

            await _tcpServer!.StartAsync().ConfigureAwait(false);
            return true;
        }

        #endregion

        #region 主动直连 (Connect 模式)

        private async Task<bool> StartConnectAsync()
        {
            _tcpClient = new TcpClient
            {
                Host = _options.TargetHost,
                Port = _options.TargetPort
            };

            _addon!.BindClient(_tcpClient);

            if (_options.AutoReconnect)
            {
                _tcpClient.UseAutoReconnect(_options.ReconnectIntervalMs, 0);
            }

            _tcpClient.OnConnectedSuccessfully += (s, e) => HandleClientConnected(_tcpClient);
            _tcpClient.OnDisconnected += (s, e) => HandleClientDisconnected(_tcpClient);

            var connResult = await _tcpClient.ConnectAsync();
            return connResult.IsSuccess;
        }

        /// <summary>
        /// 主动向指定的目标 Slave 节点发起直连，并加入管理列表
        /// </summary>
        public async Task<SlaveSession?> ConnectToSlaveAsync(string host, int port)
        {
            if (_terminal == null || _addon == null)
            {
                lock (_lock)
                {
                    if (_terminal == null || _addon == null)
                    {
                        _terminal = new RpcTerminal(new TerminalInfo { Name = _options.NodeName });
                        _addon = new BytesRpcTerminalAddon(_terminal);

                        if (_masterService == null)
                        {
                            _masterService = new MasterScreenService();
                            _masterService.SlaveRegistered += OnSlaveReportedRegistration;
                            _terminal.RegisterService(_masterService);
                        }
                    }
                }
            }

            var client = new TcpClient
            {
                Host = host,
                Port = port
            };

            _addon!.BindClient(client);

            if (_options.AutoReconnect)
            {
                client.UseAutoReconnect(_options.ReconnectIntervalMs, 0);
            }

            string clientKey = $"{host}:{port}";
            _outboundClients[clientKey] = client;

            client.OnConnectedSuccessfully += (s, e) => HandleClientConnected(client);
            client.OnDisconnected += (s, e) => HandleClientDisconnected(client);

            var result = await client.ConnectAsync();
            if (result.IsSuccess)
            {
                string sessionId = client.GetHashCode().ToString();
                return _sessionManager.GetSession(sessionId);
            }

            return null;
        }

        #endregion

        private void HandleClientConnected(BytesClient client)
        {
            string sessionId = client.GetHashCode().ToString();
            string remoteAddress;
            if (client is TcpClient tcp)
            {
                remoteAddress = $"{tcp.Host}:{tcp.Port}";
            }
            else if (client is PeerClient peer)
            {
                remoteAddress = !string.IsNullOrWhiteSpace(peer.RemotePeerId) ? $"P2P:{peer.RemotePeerId}" : "P2P:Relay";
            }
            else
            {
                remoteAddress = "RemoteClient";
            }

            var session = new SlaveSession(sessionId, client, remoteAddress);
            _sessionManager.AddOrUpdate(session);

            // 异步等待对端服务注册完成并获取透明 RPC 代理
            Task.Run(async () =>
            {
                await Task.Delay(100);

                try
                {
                    if (_addon != null && client.IsConnected)
                    {
                        // 获取屏幕服务代理
                        var screenProxy = _addon.QueryService<IScreenShareService>(nameof(ScreenShareService), client);
                        if (screenProxy != null)
                        {
                            session.BindScreenService(screenProxy);

                            try
                            {
                                var regInfo = screenProxy.GetRegistrationInfo();
                                if (regInfo != null)
                                {
                                    session.Info = regInfo;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Master Warning] GetRegistrationInfo: {ex.Message}");
                            }
                        }

                        // 仅当分享模式为完全控制时，才获取输入控制服务代理
                        if (session.Info.ShareMode == ScreenShareMode.FullControl)
                        {
                            try
                            {
                                var inputProxy = _addon.QueryService<IInputControlService>(nameof(InputControlService), client);
                                session.InputService = inputProxy;
                            }
                            catch
                            {
                                session.InputService = null;
                            }
                        }
                        else
                        {
                            session.InputService = null;
                        }

                        _sessionManager.AddOrUpdate(session);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Master Error] QueryService failed: {ex.Message}");
                    _sessionManager.AddOrUpdate(session);
                }
            });
        }

        private void OnSlaveReportedRegistration(object? sender, SlaveNodeRegistrationDto e)
        {
            if (e == null) return;

            // 校验 TargetMasterPeerId 寻址安全性
            if (!string.IsNullOrWhiteSpace(e.TargetMasterPeerId) && !string.IsNullOrWhiteSpace(LocalPeerId))
            {
                if (!string.Equals(e.TargetMasterPeerId.Trim(), LocalPeerId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Master Security] 拒绝接入：Slave 目标 PeerId '{e.TargetMasterPeerId}' 与本机 '{LocalPeerId}' 不匹配。");
                    return;
                }
            }

            // 若在 P2P Relay 客户端模式下且尚未注册会话，立即创建会话
            if (_p2pClient != null && _p2pClient.IsConnected && !_sessionManager.Sessions.Any())
            {
                HandleClientConnected(_p2pClient);
            }

            // 当 Slave 上线主动上报时，更新会话元数据
            foreach (var session in _sessionManager.Sessions)
            {
                if (session.Info.SlaveId == e.SlaveId || session.Info.MachineName == e.MachineName || _sessionManager.Sessions.Count == 1)
                {
                    session.Info = e;
                    if (e.ShareMode == ScreenShareMode.ViewOnly)
                    {
                        session.InputService = null;
                    }
                    _sessionManager.AddOrUpdate(session);
                    return;
                }
            }

            // 若尚未关联，则赋给最近加入的会话
            foreach (var session in _sessionManager.Sessions)
            {
                if (string.IsNullOrEmpty(session.Info.Note) || session.Info.SlaveId == session.SessionId)
                {
                    session.Info = e;
                    if (e.ShareMode == ScreenShareMode.ViewOnly)
                    {
                        session.InputService = null;
                    }
                    _sessionManager.AddOrUpdate(session);
                    break;
                }
            }
        }

        private void HandleClientDisconnected(BytesClient client)
        {
            string sessionId = client.GetHashCode().ToString();
            _sessionManager.Remove(sessionId);
        }

        /// <summary>
        /// 异步停止服务监听与所有连接并释放资源
        /// </summary>
        public async Task StopAsync()
        {
            TcpServer? serverToClose = null;
            TcpClient? clientToClose = null;
            BootstrapServer? bootstrapToClose = null;
            PeerClient? peerToClose = null;

            lock (_lock)
            {
                _initialConnectCts?.Cancel();
                _initialConnectCts = null;

                _p2pWatcherCts?.Cancel();
                _p2pWatcherCts = null;

                if (_tcpServer != null)
                {
                    serverToClose = _tcpServer;
                    _tcpServer = null;
                }

                if (_tcpClient != null)
                {
                    clientToClose = _tcpClient;
                    _tcpClient = null;
                }

                if (_bootstrapServer != null)
                {
                    bootstrapToClose = _bootstrapServer;
                    _bootstrapServer = null;
                }

                if (_p2pClient != null)
                {
                    peerToClose = _p2pClient;
                    _p2pClient = null;
                }

                foreach (var kvp in _outboundClients)
                {
                    try
                    {
                        kvp.Value.Disconnect();
                        kvp.Value.Dispose();
                    }
                    catch { }
                }
                _outboundClients.Clear();

                if (_masterService != null)
                {
                    _masterService.SlaveRegistered -= OnSlaveReportedRegistration;
                    _masterService = null;
                }

                _sessionManager.Clear();

                try
                {
                    _addon?.Dispose();
                }
                catch { }
                _addon = null;
                _terminal = null;
            }

            if (serverToClose != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(2000);
                    var closeTask = serverToClose.CloseAsync();
                    await Task.WhenAny(closeTask, Task.Delay(2000, cts.Token)).ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    try { serverToClose.Dispose(); } catch { }
                }
            }

            if (clientToClose != null)
            {
                try
                {
                    clientToClose.Disconnect();
                    clientToClose.Dispose();
                }
                catch { }
            }

            if (bootstrapToClose != null)
            {
                try { bootstrapToClose.Dispose(); } catch { }
            }

            if (peerToClose != null)
            {
                try { peerToClose.Disconnect(); } catch { }
                try { peerToClose.Dispose(); } catch { }
            }
        }

        public void Stop()
        {
            Task.Run(async () => await StopAsync()).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
