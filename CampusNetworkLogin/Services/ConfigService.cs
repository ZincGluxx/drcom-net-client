using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using CampusNetworkLogin.Models;

namespace CampusNetworkLogin.Services;

/// <summary>
/// 配置读写服务 - 使用JSON文件存储，密码加密，原生异步IO
/// </summary>
public class ConfigService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private static readonly byte[] Entropy = [0x43, 0x61, 0x6D, 0x70, 0x75, 0x73, 0x4E, 0x65, 0x74]; // "CampusNet"

    public ConfigService()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CampusNetworkLogin");
        _configPath = Path.Combine(_configDir, "config.json");
    }

    /// <summary>
    /// 异步加载配置
    /// </summary>
    public async Task<ConfigModel> LoadAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
                return new ConfigModel();

            string json;
            using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var config = JsonSerializer.Deserialize(json, CampusNetworkLogin.Helpers.AppJsonContext.Default.ConfigModel) ?? new ConfigModel();

            // 解密密码
            if (!string.IsNullOrEmpty(config.Password))
            {
                try
                {
                    var encrypted = Convert.FromBase64String(config.Password);
                    var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                    config.Password = Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    config.Password = "";
                }
            }

            return config;
        }
        catch
        {
            return new ConfigModel();
        }
    }

    /// <summary>
    /// 异步保存配置
    /// </summary>
    public async Task SaveAsync(ConfigModel config)
    {
        try
        {
            if (!Directory.Exists(_configDir))
                Directory.CreateDirectory(_configDir);

            // 加密密码后保存
            var configToSave = new ConfigModel
            {
                Server = config.Server,
                Username = config.Username,
                HostIp = config.HostIp,
                Mac = config.Mac,
                Gateway = config.Gateway,
                HostName = config.HostName,
                HostOs = config.HostOs,
                PrimaryDns = config.PrimaryDns,
                DhcpServer = config.DhcpServer,
                AutoLogin = config.AutoLogin,
                StartWithWindows = config.StartWithWindows,
                MinimizeToTray = config.MinimizeToTray,
            };

            if (!string.IsNullOrEmpty(config.Password))
            {
                var plainBytes = Encoding.UTF8.GetBytes(config.Password);
                var encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                configToSave.Password = Convert.ToBase64String(encrypted);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(configToSave, typeof(ConfigModel), new CampusNetworkLogin.Helpers.AppJsonContext(options));
            var tempPath = _configPath + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
            }

            if (File.Exists(_configPath))
                File.Replace(tempPath, _configPath, null);
            else
                File.Move(tempPath, _configPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
        }
    }
}
