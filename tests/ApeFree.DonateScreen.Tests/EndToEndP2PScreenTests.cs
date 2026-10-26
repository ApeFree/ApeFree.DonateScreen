using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.P2P;
using ApeFree.DonateScreen.Slave;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class EndToEndP2PScreenTests
    {
        [TestMethod]
        public async Task TestP2PRelayFullPipeline()
        {
            int relayPort = 19835;

            // 1. 启动独立的 P2P Relay 服务端
            using var relayServer = new ScreenRelayServer(relayPort, "E2E.Relay.TestSeed");
            bool relayStarted = await relayServer.StartAsync();
            Assert.IsTrue(relayStarted, "P2P Relay 服务端启动失败");

            // 2. 启动 Master 节点（连接 P2P Relay）
            var masterOptions = new ScreenMasterOptions
            {
                UseP2P = true,
                P2PServerAddress = $"127.0.0.1:{relayPort}",
                ServerSeed = "E2E.Relay.TestSeed",
                IdentitySeed = "E2E.Master.FixedSeed",
                NodeName = "P2PTestMaster"
            };
            using var master = new ScreenMasterNode(masterOptions);
            bool masterStarted = await master.StartAsync();
            Assert.IsTrue(masterStarted, "Master 启动 P2P 失败");
            Assert.IsFalse(string.IsNullOrWhiteSpace(master.LocalPeerId), "Master LocalPeerId 不能为空");

            // 3. 启动 Slave 节点（通过 P2P Relay 连接 Master）
            var slaveOptions = new ScreenSlaveOptions
            {
                UseP2P = true,
                P2PServerAddress = $"127.0.0.1:{relayPort}",
                ServerSeed = "E2E.Relay.TestSeed",
                TargetMasterPeerId = master.LocalPeerId,
                IdentitySeed = "E2E.Slave.FixedSeed",
                ShareMode = ScreenShareMode.FullControl,
                NodeName = "P2PTestSlave",
                AutoReconnect = false
            };
            using var slave = new ScreenSlaveNode(slaveOptions);

            var slaveConnectedTcs = new TaskCompletionSource<bool>();
            slave.ConnectionStatusChanged += (s, connected) =>
            {
                if (connected) slaveConnectedTcs.TrySetResult(true);
            };

            bool slaveStarted = await slave.StartAsync();
            Assert.IsTrue(slaveStarted, "Slave 启动 P2P 连接失败");

            // 等待连接建立与握手
            await Task.WhenAny(slaveConnectedTcs.Task, Task.Delay(5000));
            Assert.IsTrue(slave.IsConnected, "Slave 未能连入 P2P 网络");

            // 4. 等待 Master 探测到客户端并创建 Session
            SlaveSession? session = null;
            for (int i = 0; i < 60; i++)
            {
                if (master.SessionManager.Sessions.Count > 0)
                {
                    session = master.SessionManager.Sessions.First();
                    if (session.ScreenService != null) break;
                }
                await Task.Delay(200);
            }

            Assert.IsNotNull(session, "Master 端未捕获到 P2P SlaveSession");
            Assert.IsNotNull(session.ScreenService, "未能成功取得 IScreenShareService 动态代理");

            // 5. 验证拓扑查询
            var topo = session.ScreenService.GetScreenTopology();
            Assert.IsNotNull(topo);
            Assert.IsTrue(topo.Width > 0);
            Assert.IsTrue(topo.Height > 0);

            // 6. 验证推流帧到达
            var frameReceivedTcs = new TaskCompletionSource<ScreenFramePacketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.FrameReceived += (s, frame) =>
            {
                frameReceivedTcs.TrySetResult(frame);
            };

            session.ScreenService.SetRefreshQuota(1);

            var completedTask = await Task.WhenAny(frameReceivedTcs.Task, Task.Delay(6000));
            Assert.AreEqual(frameReceivedTcs.Task, completedTask, "未能通过 P2P 通道接收到 Slave 推送的屏幕数据帧");

            var frame = await frameReceivedTcs.Task;
            Assert.IsNotNull(frame);
            Assert.IsNotNull(frame.VideoData);
            Assert.IsTrue(frame.VideoData.Length > 0);

            // 7. 验证输入控制调用
            Assert.IsTrue(session.CanControl);
            Assert.IsNotNull(session.InputService);
            session.InputService.ResetInputState();

            // 8. 停止节点并释放
            await slave.StartAsync(); // no-op if connected
            slave.Stop();
            await master.StopAsync();
            relayServer.Stop();
        }
    }
}
