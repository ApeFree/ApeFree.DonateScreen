using ApeFree.DonateScreen.P2P;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class P2PUnitTests
    {
        [TestMethod]
        public void TestP2PAddressHelper_ToMultiaddr()
        {
            Assert.AreEqual("/ip4/127.0.0.1/tcp/9832", P2PAddressHelper.ToMultiaddr("127.0.0.1:9832"));
            Assert.AreEqual("/ip4/192.168.1.100/tcp/9832", P2PAddressHelper.ToMultiaddr("192.168.1.100"));
            Assert.AreEqual("/dns4/relay.apefree.org/tcp/9832", P2PAddressHelper.ToMultiaddr("relay.apefree.org:9832"));
            Assert.AreEqual("/ip4/127.0.0.1/tcp/9832/p2p/QmTestPeerId", P2PAddressHelper.ToMultiaddr("127.0.0.1:9832/p2p/QmTestPeerId"));
            Assert.AreEqual("/ip4/127.0.0.1/tcp/9832/p2p/12D3KooWTest", P2PAddressHelper.ToMultiaddr("127.0.0.1:9832", "12D3KooWTest"));
        }

        [TestMethod]
        public void TestP2PAddressHelper_BuildRelayAddress()
        {
            var addr = P2PAddressHelper.BuildRelayAddress("127.0.0.1:9832", "12D3KooWTargetPeer");
            Assert.AreEqual("/ip4/127.0.0.1/tcp/9832/p2p-circuit/p2p/12D3KooWTargetPeer", addr);
        }

        [TestMethod]
        public void TestP2PAddressHelper_SeedAndPeerId()
        {
            var seedBytes = P2PAddressHelper.ParseSeed("TestSeedString123");
            Assert.IsNotNull(seedBytes);
            Assert.AreEqual(32, seedBytes.Length);

            var peerId1 = P2PAddressHelper.GetPeerIdFromSeed("TestSeedString123");
            var peerId2 = P2PAddressHelper.GetPeerIdFromSeed("TestSeedString123");
            Assert.IsNotNull(peerId1);
            Assert.AreEqual(peerId1, peerId2, "相同种子推导出的 PeerId 必须完全一致");
            Assert.IsTrue(peerId1.StartsWith("12D3KooW"), "Libp2p PeerId 应符合标准前缀");
        }

        [TestMethod]
        public void TestP2PAddressHelper_ResolveServerMultiaddr()
        {
            var resolved = P2PAddressHelper.ResolveServerMultiaddr("127.0.0.1:9832");
            Assert.IsTrue(resolved.Contains("/p2p/"), "解析后的服务端地址必须包含 PeerId");
            Assert.IsTrue(resolved.StartsWith("/ip4/127.0.0.1/tcp/9832/p2p/"));
        }

        [TestMethod]
        public void TestP2PRelayManager_HandshakePacket()
        {
            var packet = P2PRelayManager.CreateHandshakePacket("Slave", "12D3KooWSource", "12D3KooWTarget");
            Assert.IsNotNull(packet);
            Assert.IsTrue(packet.Length > 8);

            // 验证魔数 SCRN (0x53, 0x43, 0x52, 0x4E)
            Assert.AreEqual(0x53, packet[0]);
            Assert.AreEqual(0x43, packet[1]);
            Assert.AreEqual(0x52, packet[2]);
            Assert.AreEqual(0x4E, packet[3]);
        }

        [TestMethod]
        public void TestMasterIdentityStorage()
        {
            var identity = MasterIdentityStorage.GetOrInitIdentity();
            Assert.IsNotNull(identity);
            Assert.IsFalse(string.IsNullOrWhiteSpace(identity.IdentitySeed));
            Assert.IsFalse(string.IsNullOrWhiteSpace(identity.PeerId));

            // 保存自定义种子并验证
            string customSeed = "MyCustomApeFreeSecretSeed999";
            var updated = MasterIdentityStorage.SaveIdentity(customSeed);
            Assert.AreEqual(customSeed, updated.IdentitySeed);
            Assert.AreEqual(P2PAddressHelper.GetPeerIdFromSeed(customSeed), updated.PeerId);

            // 重新读取
            var reloaded = MasterIdentityStorage.GetOrInitIdentity();
            Assert.AreEqual(customSeed, reloaded.IdentitySeed);
            Assert.AreEqual(updated.PeerId, reloaded.PeerId);
        }
    }
}
