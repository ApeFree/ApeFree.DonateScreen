using System;
using System.IO;
using System.Text.Json;

namespace ApeFree.DonateScreen.P2P
{
    /// <summary>
    /// Master 端私钥凭据与 PeerId 持久化存储管理器。
    /// 确保 Master 节点跨进程重启后保持唯一且稳定的 PeerId。
    /// </summary>
    public static class MasterIdentityStorage
    {
        private static readonly object _fileLock = new();

        private static string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "ApeFree.DonateScreen");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "master-identity.json");
        }

        public class IdentityModel
        {
            public string IdentitySeed { get; set; } = string.Empty;
            public string PeerId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取或初始化 Master 的持久化身份（若不存在则自动生成新种子并保存）
        /// </summary>
        /// <returns>身份信息模型</returns>
        public static IdentityModel GetOrInitIdentity()
        {
            lock (_fileLock)
            {
                var filePath = GetConfigFilePath();
                if (File.Exists(filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var model = JsonSerializer.Deserialize<IdentityModel>(json);
                        if (model != null && !string.IsNullOrWhiteSpace(model.IdentitySeed))
                        {
                            if (string.IsNullOrWhiteSpace(model.PeerId))
                            {
                                model.PeerId = P2PAddressHelper.GetPeerIdFromSeed(model.IdentitySeed) ?? string.Empty;
                            }
                            return model;
                        }
                    }
                    catch { }
                }

                // 首次运行，生成全新唯一的身份种子
                var seed = $"Master_{Environment.MachineName}_{Guid.NewGuid():N}";
                var peerId = P2PAddressHelper.GetPeerIdFromSeed(seed) ?? string.Empty;

                var newModel = new IdentityModel
                {
                    IdentitySeed = seed,
                    PeerId = peerId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                SaveIdentity(newModel.IdentitySeed);
                return newModel;
            }
        }

        /// <summary>
        /// 保存或更新自定义的 Master 身份种子
        /// </summary>
        /// <param name="customSeed">种子口令或十六进制私钥</param>
        /// <returns>更新后的身份模型</returns>
        public static IdentityModel SaveIdentity(string customSeed)
        {
            if (string.IsNullOrWhiteSpace(customSeed))
            {
                throw new ArgumentException("身份种子不能为空", nameof(customSeed));
            }

            lock (_fileLock)
            {
                var filePath = GetConfigFilePath();
                var peerId = P2PAddressHelper.GetPeerIdFromSeed(customSeed) ?? string.Empty;

                var model = new IdentityModel
                {
                    IdentitySeed = customSeed.Trim(),
                    PeerId = peerId,
                    UpdatedAt = DateTime.UtcNow
                };

                try
                {
                    var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json);
                }
                catch { }

                return model;
            }
        }
    }
}
