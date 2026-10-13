using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.Slave;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class EndToEndRpcScreenTests
    {
        [TestMethod]
        public async Task TestFullControlRpcPipeline()
        {
            int testPort = 19822;

            // 1. 启动 Master 节点
            var masterOptions = new ScreenMasterOptions
            {
                ListenHost = "127.0.0.1",
                ListenPort = testPort,
                NodeName = "TestMaster"
            };
            using var master = new ScreenMasterNode(masterOptions);
            bool started = await master.StartAsync();
            Assert.IsTrue(started, "Master 启动失败");

            // 2. 启动 Slave 节点（完全控制模式）
            var slaveOptions = new ScreenSlaveOptions
            {
                MasterHost = "127.0.0.1",
                MasterPort = testPort,
                ShareMode = ScreenShareMode.FullControl,
                NodeName = "TestSlave",
                AutoReconnect = false
            };
            using var slave = new ScreenSlaveNode(slaveOptions);

            var slaveConnectedTcs = new TaskCompletionSource<bool>();
            slave.ConnectionStatusChanged += (s, connected) =>
            {
                if (connected) slaveConnectedTcs.TrySetResult(true);
            };

            bool slaveStarted = await slave.StartAsync();
            Assert.IsTrue(slaveStarted, "Slave 启动连接失败");

            // 等待连接并握手
            await Task.WhenAny(slaveConnectedTcs.Task, Task.Delay(3000));
            Assert.IsTrue(slave.IsConnected, "Slave 未能成功连接至 Master");

            // 等待 Master 探测到客户端并创建 Session
            SlaveSession? session = null;
            for (int i = 0; i < 30; i++)
            {
                if (master.SessionManager.Sessions.Count > 0)
                {
                    session = System.Linq.Enumerable.First(master.SessionManager.Sessions);
                    if (session.ScreenService != null) break;
                }
                await Task.Delay(100);
            }

            Assert.IsNotNull(session, "Master 端未捕获到 SlaveSession");
            Assert.IsNotNull(session.ScreenService, "未能成功取得 IScreenShareService 动态代理");

            // 3. 测试通过代理调用 GetScreenTopology
            var topo = session.ScreenService.GetScreenTopology();
            Assert.IsNotNull(topo);
            Assert.IsTrue(topo.Width > 0);
            Assert.IsTrue(topo.Height > 0);

            // 4. 测试屏幕帧事件推送
            var frameReceivedTcs = new TaskCompletionSource<ScreenFramePacketDto>();
            session.FrameReceived += (s, frame) =>
            {
                frameReceivedTcs.TrySetResult(frame);
            };

            // 手动请求一次关键帧
            session.ScreenService.RequestKeyFrame();

            var completedTask = await Task.WhenAny(frameReceivedTcs.Task, Task.Delay(5000));
            Assert.AreEqual(frameReceivedTcs.Task, completedTask, "未能接收到 Slave 推送的屏幕数据帧");
            var frame = await frameReceivedTcs.Task;
            Assert.IsNotNull(frame);
            Assert.IsTrue(frame.Blocks.Count > 0);
            Assert.IsTrue(frame.Blocks[0].CompressedData.Length > 0);

            // 5. 测试通过代理调用键鼠控制
            Assert.IsTrue(session.CanControl, "完全控制模式下 session.CanControl 应为 true");
            Assert.IsNotNull(session.InputService);

            // 调用输入复位与鼠标事件
            session.InputService.ResetInputState();
            session.InputService.SendMouseEvent(new MouseEventDto
            {
                NormalizedX = 32768,
                NormalizedY = 32768,
                Flags = MouseActionFlags.Move
            });

            // 停止
            slave.Stop();
            master.Stop();
        }

        [TestMethod]
        public async Task TestViewOnlyModeBlocksInputControl()
        {
            int testPort = 19823;

            var masterOptions = new ScreenMasterOptions
            {
                ListenHost = "127.0.0.1",
                ListenPort = testPort,
                NodeName = "TestMasterViewOnly"
            };
            using var master = new ScreenMasterNode(masterOptions);
            await master.StartAsync();

            // 启动 Slave 节点（仅观看模式）
            var slaveOptions = new ScreenSlaveOptions
            {
                MasterHost = "127.0.0.1",
                MasterPort = testPort,
                ShareMode = ScreenShareMode.ViewOnly,
                NodeName = "TestSlaveViewOnly",
                AutoReconnect = false
            };
            using var slave = new ScreenSlaveNode(slaveOptions);
            await slave.StartAsync();

            SlaveSession? session = null;
            for (int i = 0; i < 30; i++)
            {
                if (master.SessionManager.Sessions.Count > 0)
                {
                    session = System.Linq.Enumerable.First(master.SessionManager.Sessions);
                    if (session.ScreenService != null && session.Info.ShareMode == ScreenShareMode.ViewOnly) break;
                }
                await Task.Delay(100);
            }

            Assert.IsNotNull(session);
            Assert.IsNotNull(session.ScreenService);

            var directInfo = session.ScreenService.GetRegistrationInfo();
            Console.WriteLine($"[Test Debug] session.Info.ShareMode={session.Info.ShareMode}, Direct.ShareMode={directInfo?.ShareMode}, InputServiceIsNull={session.InputService == null}");

            // 验证仅观看模式下 InputService 为 null 且 CanControl 为 false
            Assert.IsFalse(session.CanControl, "仅观看模式下 CanControl 应为 false");
            Assert.IsNull(session.InputService, "仅观看模式下不应暴露 InputService 动态代理");

            slave.Stop();
            master.Stop();
        }
    }
}
