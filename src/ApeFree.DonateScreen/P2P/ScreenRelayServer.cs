using STTech.BytesIO.P2P;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.P2P
{
    /// <summary>
    /// P2P 引导与中继服务端宿主 (Screen Relay Server)。
    /// 封装 BootstrapServer 与 P2PRelayManager，提供全双工数据中继与会话精准配对。
    /// </summary>
    public class ScreenRelayServer : IDisposable
    {
        private readonly object _lock = new();
        private BootstrapServer? _server;
        private P2PRelayManager? _relayManager;
        private bool _isDisposed;

        /// <summary>
        /// 监听主机地址 (默认 0.0.0.0 监听所有网卡)
        /// </summary>
        public string Host { get; set; } = "0.0.0.0";

        /// <summary>
        /// 监听端口号 (默认 9832)
        /// </summary>
        public int Port { get; set; } = P2PAddressHelper.DefaultP2PPort;

        /// <summary>
        /// 服务端凭据种子 (若为空则采用默认预设种子)
        /// </summary>
        public string? IdentitySeed { get; set; }

        /// <summary>
        /// 是否启用 QUIC 传输
        /// </summary>
        public bool EnableQuic { get; set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _server != null && _server.IsListening;

        /// <summary>
        /// 服务端自身的 PeerId
        /// </summary>
        public string? LocalPeerId => _server?.LocalPeerId;

        /// <summary>
        /// 当前在线连接的节点数量
        /// </summary>
        public int ActivePeerCount => _relayManager?.ActivePeerCount ?? 0;

        /// <summary>
        /// 当前已成功配对的点对点会话对数
        /// </summary>
        public int PairedSessionCount => _relayManager?.PairedSessionCount ?? 0;

        /// <summary>
        /// 运行状态变更事件 (isRunning)
        /// </summary>
        public event EventHandler<bool>? StatusChanged;

        /// <summary>
        /// 在线对等节点数变更事件
        /// </summary>
        public event EventHandler<int>? PeerCountChanged;

        /// <summary>
        /// 服务日志消息事件
        /// </summary>
        public event EventHandler<string>? LogMessage;

        public ScreenRelayServer()
        {
        }

        public ScreenRelayServer(int port, string? seed = null)
        {
            Port = port;
            IdentitySeed = seed;
        }

        /// <summary>
        /// 启动中继服务端
        /// </summary>
        public async Task<bool> StartAsync()
        {
            lock (_lock)
            {
                if (IsRunning) return true;

                _relayManager?.Dispose();
                _relayManager = new P2PRelayManager();
                _relayManager.ActivePeerCountChanged += (s, count) => PeerCountChanged?.Invoke(this, count);
                _relayManager.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

                _server?.Dispose();
                _server = new BootstrapServer();
                _server.Options.ListenAddress = Host;
                _server.Options.TcpPort = Port;
                _server.Options.EnableRelay = false;
                _server.Options.EnableDht = true;
                _server.Options.EnableMdns = true;
                _server.Options.EnableQuic = EnableQuic;

                string effectiveSeed = !string.IsNullOrWhiteSpace(IdentitySeed) ? IdentitySeed : P2PAddressHelper.DefaultServerSeed;
                _server.Options.IdentitySeed = P2PAddressHelper.ParseSeed(effectiveSeed);

                _server.PeerConnected += (s, e) =>
                {
                    _relayManager.RegisterClient(e.Client);
                };

                _server.PeerDisconnected += (s, e) =>
                {
                    _relayManager.UnregisterClient(e.Client);
                };
            }

            try
            {
                await _server.StartAsync().ConfigureAwait(false);
                LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [服务启动] P2P Relay 服务端已在 {Host}:{Port} 启动监听 (PeerId: {_server.LocalPeerId})");
                StatusChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [启动失败] 异常: {ex.Message}");
                StatusChanged?.Invoke(this, false);
                return false;
            }
        }

        /// <summary>
        /// 停止中继服务端
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_server != null)
                {
                    try { _server.Dispose(); } catch { }
                    _server = null;
                }

                if (_relayManager != null)
                {
                    _relayManager.Dispose();
                    _relayManager = null;
                }
            }

            LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [服务停止] P2P Relay 服务端已停止运行。");
            StatusChanged?.Invoke(this, false);
            PeerCountChanged?.Invoke(this, 0);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
