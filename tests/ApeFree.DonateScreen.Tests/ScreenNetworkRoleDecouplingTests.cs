using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.Slave;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class ScreenNetworkRoleDecouplingTests
    {
        /// <summary>
        /// 测试场景 1：正向直连（Slave 开放端口监听作为受控服务，Master 作为客户端主动直连并开展屏幕监控与控制）
        /// </summary>
        [TestMethod]
        public async Task TestForwardDirectConnect_SlaveListen_MasterConnect_ShouldSucceed()
        {
            int testPort = 19824;

            // 1. 启动 Slave 服务端监听
            var slaveOptions = new ScreenSlaveOptions
            {
                TransportMode = NetworkTransportMode.Listen,
                ListenHost = "127.0.0.1",
                ListenPort = testPort,
                ShareMode = ScreenShareMode.FullControl,
                NodeName = "ForwardSlave"
            };

            using var slave = new ScreenSlaveNode(slaveOptions);
            bool slaveStarted = await slave.StartAsync();
            Assert.IsTrue(slaveStarted, "Slave 监听端启动失败");
            Assert.IsTrue(slave.IsListening, "Slave 应当处于监听状态");

            // 2. 启动 Master 客户端主动连入
            var masterOptions = new ScreenMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort,
                NodeName = "ForwardMaster",
                AutoReconnect = false
            };

            using var master = new ScreenMasterNode(masterOptions);
            await master.StartAsync();

            // 等待 Master 成功连上 Slave 并创建 Session
            SlaveSession? session = null;
            for (int i = 0; i < 40; i++)
            {
                if (master.SessionManager.Sessions.Count > 0)
                {
                    session = master.SessionManager.Sessions.FirstOrDefault();
                    if (session?.ScreenService != null) break;
                }
                await Task.Delay(100);
            }

            Assert.IsNotNull(session, "Master 端未捕获到 SlaveSession");
            Assert.IsNotNull(session.ScreenService, "未能成功取得 IScreenShareService 动态代理");
            Assert.AreEqual(1, slave.ConnectedMasterCount, "Slave 端的已连接 Master 计数应为 1");

            // 3. 验证屏幕拓扑与配额推流
            var topo = session.ScreenService.GetScreenTopology();
            Assert.IsNotNull(topo);
            Assert.IsTrue(topo.Width > 0);
            Assert.IsTrue(topo.Height > 0);

            var frameReceivedTcs = new TaskCompletionSource<ScreenFramePacketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.FrameReceived += (s, frame) =>
            {
                frameReceivedTcs.TrySetResult(frame);
            };

            await Task.Delay(200);
            session.ScreenService.SetRefreshQuota(5);

            var completedTask = await Task.WhenAny(frameReceivedTcs.Task, Task.Delay(5000));
            Assert.AreEqual(frameReceivedTcs.Task, completedTask, "未能接收到 Slave 推送的屏幕数据帧");

            var frame = await frameReceivedTcs.Task;
            Assert.IsNotNull(frame);
            Assert.IsTrue(frame.VideoData.Length > 0);

            // 4. 验证输入控制
            Assert.IsTrue(session.CanControl);
            Assert.IsNotNull(session.InputService);
            session.InputService.ResetInputState();

            // 5. 停止
            await master.StopAsync();
            await slave.StopAsync();
        }

        /// <summary>
        /// 测试场景 2：多 Master 并发连入同一个 Slave 监听端
        /// </summary>
        [TestMethod]
        public async Task TestMultiMasterConcurrent_SlaveListen_ShouldServeMultipleMasters()
        {
            int testPort = 19825;

            // 1. 启动 Slave 服务端监听
            var slaveOptions = new ScreenSlaveOptions
            {
                TransportMode = NetworkTransportMode.Listen,
                ListenHost = "127.0.0.1",
                ListenPort = testPort,
                ShareMode = ScreenShareMode.FullControl,
                NodeName = "MultiMasterSlave"
            };

            using var slave = new ScreenSlaveNode(slaveOptions);
            await slave.StartAsync();

            // 2. 启动 Master 1
            var master1Options = new ScreenMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort,
                NodeName = "Master1",
                AutoReconnect = false
            };
            using var master1 = new ScreenMasterNode(master1Options);
            await master1.StartAsync();

            // 3. 启动 Master 2
            var master2Options = new ScreenMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort,
                NodeName = "Master2",
                AutoReconnect = false
            };
            using var master2 = new ScreenMasterNode(master2Options);
            await master2.StartAsync();

            // 等待两个 Master 均与 Slave 建立会话
            SlaveSession? session1 = null;
            SlaveSession? session2 = null;

            for (int i = 0; i < 40; i++)
            {
                if (session1 == null && master1.SessionManager.Sessions.Count > 0)
                {
                    var s = master1.SessionManager.Sessions.FirstOrDefault();
                    if (s?.ScreenService != null) session1 = s;
                }

                if (session2 == null && master2.SessionManager.Sessions.Count > 0)
                {
                    var s = master2.SessionManager.Sessions.FirstOrDefault();
                    if (s?.ScreenService != null) session2 = s;
                }

                if (session1 != null && session2 != null) break;
                await Task.Delay(100);
            }

            Assert.IsNotNull(session1, "Master 1 应当已建立会话");
            Assert.IsNotNull(session2, "Master 2 应当已建立会话");
            Assert.AreEqual(2, slave.ConnectedMasterCount, "Slave 端的已连接 Master 计数应为 2");

            // 4. 断开 Master 1
            await master1.StopAsync();
            await Task.Delay(300);

            Assert.AreEqual(1, slave.ConnectedMasterCount, "断开 Master 1 后 Slave 端的计数应降为 1");

            // 5. 验证 Master 2 依然能正常请求并接收屏幕帧
            var frame2Tcs = new TaskCompletionSource<ScreenFramePacketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            session2.FrameReceived += (s, frame) =>
            {
                frame2Tcs.TrySetResult(frame);
            };

            session2.ScreenService!.SetRefreshQuota(1);

            var completed = await Task.WhenAny(frame2Tcs.Task, Task.Delay(5000));
            Assert.AreEqual(frame2Tcs.Task, completed, "Master 2 应该依然能正常接收屏幕帧");

            await master2.StopAsync();
            await slave.StopAsync();
        }
    }
}
