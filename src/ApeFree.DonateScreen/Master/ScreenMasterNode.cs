using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Slave;
using STTech.BytesIO.Core;
using STTech.BytesIO.Tcp;
using System;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 主控端服务监听节点。
    /// 负责开启 TCP 监听、管理连入的 Slave 客户端并提供动态服务代理。
    /// </summary>
    public class ScreenMasterNode : IDisposable
    {
        private readonly ScreenMasterOptions _options;
        private readonly object _lock = new();
        private RpcTerminal? _terminal;
        private TcpServer? _tcpServer;
        private BytesRpcTerminalAddon? _addon;
        private MasterScreenService? _masterService;
        private readonly SlaveSessionManager _sessionManager = new();
        private bool _isDisposed;

        /// <summary>
        /// Slave 多设备会话管理器
        /// </summary>
        public SlaveSessionManager SessionManager => _sessionManager;

        /// <summary>
        /// 是否正在运行监听
        /// </summary>
        public bool IsRunning => _tcpServer != null && _tcpServer.IsListening;

        /// <summary>
        /// 配置参数
        /// </summary>
        public ScreenMasterOptions Options => _options;

        public ScreenMasterNode(ScreenMasterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 启动 TCP 服务端监听
        /// </summary>
        public async Task<bool> StartAsync()
        {
            lock (_lock)
            {
                if (IsRunning) return true;

                // 1. 初始化终端
                _terminal = new RpcTerminal(new TerminalInfo { Name = _options.NodeName });

                // 2. 创建 TcpServer
                _tcpServer = new TcpServer
                {
                    Host = _options.ListenHost,
                    Port = _options.ListenPort
                };

                // 3. 挂载 BytesRpc 插件并绑定服务端监听器
                _addon = new BytesRpcTerminalAddon(_terminal);
                _addon.BindServer(_tcpServer);

                // 4. 注册协同服务
                _masterService = new MasterScreenService();
                _masterService.SlaveRegistered += OnSlaveReportedRegistration;
                _terminal.RegisterService(_masterService);

                // 5. 监听客户端上下线
                _tcpServer.ClientConnected += (s, e) => HandleClientConnected(e.Client);
                _tcpServer.ClientDisconnected += (s, e) => HandleClientDisconnected(e.Client);
            }

            await _tcpServer.StartAsync();
            return true;
        }

        private void HandleClientConnected(BytesClient client)
        {
            string sessionId = client.GetHashCode().ToString();
            string remoteAddress = (client is TcpClient tcp) ? $"{tcp.Host}:{tcp.Port}" : "RemoteClient";

            var session = new SlaveSession(sessionId, client, remoteAddress);

            // 延迟一点等待 Slave 服务注册就绪后获取透明代理
            Task.Run(async () =>
            {
                await Task.Delay(300);

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
                                Console.WriteLine($"[Master Error in GetRegistrationInfo] {ex}");
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
                catch (Exception)
                {
                    // 若初次获取异常，仍将基础会话添加，等待 Slave 上报注册信息
                    _sessionManager.AddOrUpdate(session);
                }
            });
        }

        private void OnSlaveReportedRegistration(object? sender, SlaveNodeRegistrationDto e)
        {
            // 当 Slave 上线主动上报时，更新会话元数据
            foreach (var session in _sessionManager.Sessions)
            {
                if (session.Info.SlaveId == e.SlaveId || session.Info.MachineName == e.MachineName)
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
        /// 异步停止服务监听并释放连接
        /// </summary>
        public async Task StopAsync()
        {
            TcpServer? serverToClose = null;
            lock (_lock)
            {
                if (_tcpServer != null)
                {
                    serverToClose = _tcpServer;
                    _tcpServer = null;
                }

                _sessionManager.Clear();

                if (_masterService != null)
                {
                    _masterService.SlaveRegistered -= OnSlaveReportedRegistration;
                    _masterService = null;
                }

                _addon?.Dispose();
                _addon = null;
                _terminal = null;
            }

            if (serverToClose != null)
            {
                await serverToClose.CloseAsync();
                serverToClose.Dispose();
            }
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
