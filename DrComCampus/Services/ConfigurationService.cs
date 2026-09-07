using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DrComCampus.Models;

namespace DrComCampus.Services;

/// <summary>
/// 配置读写服务 - 使用JSON文件存储，密码加密，原生异步IO
/// </summary>
internal sealed class ConfigurationService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string _legacyConfigPath;
    private static readonly byte[] s_entropy = [0x43, 0x61, 0x6D, 0x70, 0x75, 0x73, 0x4E, 0x65, 0x74]; // "CampusNet"
    private static readonly DrComCampus.Helpers.AppJsonContext s_serializerContext =
        new(new JsonSerializerOptions { WriteIndented = true });

    public ConfigurationService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configDir = Path.Combine(localAppData, "DrComCampus");
        _configPath = Path.Combine(_configDir, "config.json");
        _legacyConfigPath = Path.Combine(localAppData, "CampusNetworkLogin", "config.json");
    }

    /// <summary>
    /// 异步加载配置
    /// </summary>
    public async Task<AppConfiguration> LoadAsync()
    {
        try
        {
            var readPath = File.Exists(_configPath) ? _configPath : _legacyConfigPath;
            if (!File.Exists(readPath))
            {
                return new AppConfiguration();
            }

            string json;
            using (var stream = new FileStream(readPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var config = JsonSerializer.Deserialize(json, s_serializerContext.AppConfiguration) ?? new AppConfiguration();

            // 解密密码
            if (!string.IsNullOrEmpty(config.Password))
            {
                try
                {
                    var encrypted = Convert.FromBase64String(config.Password);
                    var decrypted = ProtectedData.Unprotect(encrypted, s_entropy, DataProtectionScope.CurrentUser);
                    config.Password = Encoding.UTF8.GetString(decrypted);
                    CryptographicOperations.ZeroMemory(decrypted);
                }
                catch
                {
                    config.Password = "";
                }
            }

            if (readPath == _legacyConfigPath)
            {
                try { await SaveAsync(config).ConfigureAwait(false); }
                catch { /* 迁移失败时仍返回已读取的旧配置 */ }
            }

            return config;
        }
        catch
        {
            return new AppConfiguration();
        }
    }

    /// <summary>
    /// 异步保存配置
    /// </summary>
    public async Task SaveAsync(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            if (!Directory.Exists(_configDir))
            {
                Directory.CreateDirectory(_configDir);
            }

            // 加密密码后保存
            var configToSave = new AppConfiguration
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
                AutoReconnect = config.AutoReconnect,
                StartWithWindows = config.StartWithWindows,
                MinimizeToTray = config.MinimizeToTray,
            };

            if (!string.IsNullOrEmpty(config.Password))
            {
                var plainBytes = Encoding.UTF8.GetBytes(config.Password);
                try
                {
                    var encrypted = ProtectedData.Protect(plainBytes, s_entropy, DataProtectionScope.CurrentUser);
                    configToSave.Password = Convert.ToBase64String(encrypted);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }

            var json = JsonSerializer.Serialize(configToSave, s_serializerContext.AppConfiguration);
            var tempPath = _configPath + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
            }

            if (File.Exists(_configPath))
            {
                File.Replace(tempPath, _configPath, null);
            }
            else
            {
                File.Move(tempPath, _configPath);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
        }
    }
}
