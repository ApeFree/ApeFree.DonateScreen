using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ApeFree.DonateScreen.P2P
{
    /// <summary>
    /// P2P 引导与中继服务端对等节点中继管理器。
    /// 支持精准 Target PeerId 会话配对 (Session Pairing) 以及默认双节点全双工字节流桥接。
    /// </summary>
    public class P2PRelayManager : IDisposable
    {
        private static readonly byte[] RelayMagic = new byte[] { 0x53, 0x43, 0x52, 0x4E }; // "SCRN"

        private readonly object _lock = new();
        private readonly Dictionary<PeerClient, ClientRegistration> _registrations = new();
        private readonly Dictionary<PeerClient, PeerClient> _pairedRoutes = new();
        private readonly Dictionary<PeerClient, EventHandler<DataReceivedEventArgs>> _handlers = new();
        private bool _isDisposed;

        /// <summary>
        /// 当中继活动节点数发生变更时触发
        /// </summary>
        public event EventHandler<int>? ActivePeerCountChanged;

        /// <summary>
        /// 当两端成功配对时触发 (Master, Slave, TargetPeerId)
        /// </summary>
        public event EventHandler<(PeerClient Master, PeerClient Slave, string TargetPeerId)>? SessionPaired;

        /// <summary>
        /// 当已配对会话解除绑定时触发
        /// </summary>
        public event EventHandler<PeerClient>? SessionUnpaired;

        /// <summary>
        /// 中继服务日志事件
        /// </summary>
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// 获取当前活动节点总数
        /// </summary>
        public int ActivePeerCount
        {
            get
            {
                lock (_lock) return _registrations.Count;
            }
        }

        /// <summary>
        /// 获取当前已成功配对的会话对数
        /// </summary>
        public int PairedSessionCount
        {
            get
            {
                lock (_lock) return _pairedRoutes.Count / 2;
            }
        }

        private class ClientRegistration
        {
            public PeerClient Client { get; set; } = null!;
            public string? DeclaredPeerId { get; set; }
            public string? TargetPeerId { get; set; }
            public bool IsMaster { get; set; }
            public bool HandshakeCompleted { get; set; }
        }

        private class HandshakePayload
        {
            public string? Role { get; set; } // "Master" or "Slave"
            public string? PeerId { get; set; }
            public string? TargetPeerId { get; set; }
        }

        /// <summary>
        /// 注册连入的对等节点并监听数据流
        /// </summary>
        public void RegisterClient(PeerClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            lock (_lock)
            {
                if (_isDisposed) return;

                if (_handlers.TryGetValue(client, out var existingHandler))
                {
                    client.OnDataReceived -= existingHandler;
                    _handlers.Remove(client);
                }

                var reg = new ClientRegistration
                {
                    Client = client,
                    DeclaredPeerId = client.RemotePeerId
                };
                _registrations[client] = reg;

                EventHandler<DataReceivedEventArgs> handler = (sender, args) => HandleDataReceived(client, args);
                client.OnDataReceived += handler;
                _handlers[client] = handler;
            }

            Log($"[节点连入] PeerId: {client.RemotePeerId ?? "未声明"} 地址: {client.RemoteAddress}");
            ActivePeerCountChanged?.Invoke(this, ActivePeerCount);

            // 检查是否有仅 2 节点且未走握手的默认自动配对机会
            TryFallbackPairing();
        }

        /// <summary>
        /// 注销断开的对等节点并释放关联配对
        /// </summary>
        public void UnregisterClient(PeerClient client)
        {
            if (client == null) return;

            PeerClient? pairedPeer = null;

            lock (_lock)
            {
                if (_handlers.TryGetValue(client, out var handler))
                {
                    try { client.OnDataReceived -= handler; } catch { }
                    _handlers.Remove(client);
                }

                _registrations.Remove(client);

                if (_pairedRoutes.TryGetValue(client, out pairedPeer))
                {
                    _pairedRoutes.Remove(client);
                    _pairedRoutes.Remove(pairedPeer);
                }
            }

            if (pairedPeer != null)
            {
                Log($"[会话解绑] 节点 {client.RemotePeerId ?? "未知"} 离线，配对会话已解除。");
                SessionUnpaired?.Invoke(this, pairedPeer);
            }

            Log($"[节点断开] PeerId: {client.RemotePeerId ?? "未声明"}");
            ActivePeerCountChanged?.Invoke(this, ActivePeerCount);
        }

        private void HandleDataReceived(PeerClient client, DataReceivedEventArgs args)
        {
            if (args?.Data == null || args.Data.Memory.Length == 0) return;
            var payload = args.Data.Memory.ToArray();

            lock (_lock)
            {
                if (_isDisposed) return;

                if (!_registrations.TryGetValue(client, out var reg)) return;

                // 检查是否为握手协议包
                if (!reg.HandshakeCompleted && IsHandshakePacket(payload, out var handshake))
                {
                    reg.HandshakeCompleted = true;
                    if (handshake != null)
                    {
                        reg.IsMaster = string.Equals(handshake.Role, "Master", StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrWhiteSpace(handshake.PeerId))
                        {
                            reg.DeclaredPeerId = handshake.PeerId;
                        }
                        reg.TargetPeerId = handshake.TargetPeerId;

                        Log($"[握手解析] 节点角色: {(reg.IsMaster ? "Master" : "Slave")} 声明PeerId: {reg.DeclaredPeerId ?? "无"} 目标PeerId: {reg.TargetPeerId ?? "无"}");
                        TryMatchTargetPairing(reg);
                    }
                    return;
                }

                // 若已处于配对路由中，直接点对点转发
                if (_pairedRoutes.TryGetValue(client, out var target) && target.IsConnected)
                {
                    try
                    {
                        target.Send(payload);
                    }
                    catch
                    {
                        // 目标发送异常
                    }
                    return;
                }

                // 若未明确配对但只有两个节点在线，作为备选透明桥接
                var activeClients = _registrations.Keys.Where(c => c != client && c.IsConnected).ToList();
                if (activeClients.Count == 1)
                {
                    try
                    {
                        activeClients[0].Send(payload);
                    }
                    catch { }
                }
            }
        }

        private bool IsHandshakePacket(byte[] data, out HandshakePayload? handshake)
        {
            handshake = null;
            if (data.Length < 8) return false;

            for (int i = 0; i < RelayMagic.Length; i++)
            {
                if (data[i] != RelayMagic[i]) return false;
            }

            int length = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
            if (length <= 0 || data.Length < 8 + length) return false;

            try
            {
                string json = Encoding.UTF8.GetString(data, 8, length);
                handshake = JsonSerializer.Deserialize<HandshakePayload>(json);
                return handshake != null;
            }
            catch
            {
                return false;
            }
        }

        private void TryMatchTargetPairing(ClientRegistration current)
        {
            // 如果是 Slave 且指定了 TargetPeerId，寻址对应 Master
            if (!current.IsMaster && !string.IsNullOrWhiteSpace(current.TargetPeerId))
            {
                var master = _registrations.Values.FirstOrDefault(r =>
                    r.IsMaster &&
                    !_pairedRoutes.ContainsKey(r.Client) &&
                    (string.Equals(r.DeclaredPeerId, current.TargetPeerId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.Client.RemotePeerId, current.TargetPeerId, StringComparison.OrdinalIgnoreCase)));

                if (master != null)
                {
                    BindPair(master.Client, current.Client, current.TargetPeerId!);
                }
            }
            // 如果是 Master，寻址等待它的 Slave
            else if (current.IsMaster && !string.IsNullOrWhiteSpace(current.DeclaredPeerId))
            {
                var waitingSlave = _registrations.Values.FirstOrDefault(r =>
                    !r.IsMaster &&
                    !_pairedRoutes.ContainsKey(r.Client) &&
                    string.Equals(r.TargetPeerId, current.DeclaredPeerId, StringComparison.OrdinalIgnoreCase));

                if (waitingSlave != null)
                {
                    BindPair(current.Client, waitingSlave.Client, current.DeclaredPeerId!);
                }
            }
        }

        private void TryFallbackPairing()
        {
            lock (_lock)
            {
                // 若刚好有两个客户端且均未单独配对
                if (_registrations.Count == 2 && _pairedRoutes.Count == 0)
                {
                    var list = _registrations.Values.ToList();
                    var c1 = list[0];
                    var c2 = list[1];

                    // 仅当两者没有冲突的目标设置时，尝试缺省配对
                    if (string.IsNullOrWhiteSpace(c1.TargetPeerId) && string.IsNullOrWhiteSpace(c2.TargetPeerId))
                    {
                        BindPair(c1.Client, c2.Client, "Default");
                    }
                }
            }
        }

        private void BindPair(PeerClient master, PeerClient slave, string targetPeerId)
        {
            _pairedRoutes[master] = slave;
            _pairedRoutes[slave] = master;

            Log($"[成功配对] Master: {master.RemotePeerId ?? "未命名"} <===> Slave: {slave.RemotePeerId ?? "未命名"} (Target: {targetPeerId})");
            SessionPaired?.Invoke(this, (master, slave, targetPeerId));
        }

        private void Log(string msg)
        {
            LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        /// <summary>
        /// 创建中继握手协议数据包
        /// </summary>
        public static byte[] CreateHandshakePacket(string role, string? peerId, string? targetPeerId)
        {
            var payload = new HandshakePayload
            {
                Role = role,
                PeerId = peerId,
                TargetPeerId = targetPeerId
            };
            string json = JsonSerializer.Serialize(payload);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            byte[] packet = new byte[8 + jsonBytes.Length];
            Array.Copy(RelayMagic, 0, packet, 0, 4);

            packet[4] = (byte)((jsonBytes.Length >> 24) & 0xFF);
            packet[5] = (byte)((jsonBytes.Length >> 16) & 0xFF);
            packet[6] = (byte)((jsonBytes.Length >> 8) & 0xFF);
            packet[7] = (byte)(jsonBytes.Length & 0xFF);

            Array.Copy(jsonBytes, 0, packet, 8, jsonBytes.Length);
            return packet;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                foreach (var kv in _handlers)
                {
                    try { kv.Key.OnDataReceived -= kv.Value; } catch { }
                }

                _handlers.Clear();
                _registrations.Clear();
                _pairedRoutes.Clear();
            }
        }
    }
}
