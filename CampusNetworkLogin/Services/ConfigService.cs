using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using CampusNetworkLogin.Models;

namespace CampusNetworkLogin.Services;

/// <summary>
/// 配置读写服务 - 使用JSON文件存储，密码加密
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
    /// 加载配置
    /// </summary>
    public ConfigModel Load()
    {
        try
        {
            if (!File.Exists(_configPath))
                return new ConfigModel();

            var json = File.ReadAllText(_configPath, Encoding.UTF8);
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
    /// 保存配置
    /// </summary>
    public void Save(ConfigModel config)
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
            File.WriteAllText(tempPath, json, Encoding.UTF8);

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
