using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ApeFree.DonateScreen.Master
{
    /// <summary>
    /// Master 端的 Slave 会话多设备接入管理器
    /// </summary>
    public class SlaveSessionManager
    {
        private readonly ConcurrentDictionary<string, SlaveSession> _sessions = new();

        /// <summary>
        /// 有新 Slave 设备接入并就绪事件
        /// </summary>
        public event EventHandler<SlaveSession>? SlaveConnected;

        /// <summary>
        /// 有 Slave 设备断开连接事件
        /// </summary>
        public event EventHandler<SlaveSession>? SlaveDisconnected;

        /// <summary>
        /// Slave 设备信息更新事件
        /// </summary>
        public event EventHandler<SlaveSession>? SlaveUpdated;

        /// <summary>
        /// 当前所有在线的 Slave 设备列表
        /// </summary>
        public IReadOnlyCollection<SlaveSession> Sessions => _sessions.Values.ToList();

        public void AddOrUpdate(SlaveSession session)
        {
            if (session == null) return;
            bool isNew = !_sessions.ContainsKey(session.SessionId);
            _sessions[session.SessionId] = session;

            if (isNew)
            {
                SlaveConnected?.Invoke(this, session);
            }
            else
            {
                SlaveUpdated?.Invoke(this, session);
            }
        }

        public SlaveSession? Remove(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Dispose();
                SlaveDisconnected?.Invoke(this, session);
                return session;
            }
            return null;
        }

        public SlaveSession? GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        public void Clear()
        {
            foreach (var kv in _sessions)
            {
                kv.Value.Dispose();
                SlaveDisconnected?.Invoke(this, kv.Value);
            }
            _sessions.Clear();
        }
    }
}
