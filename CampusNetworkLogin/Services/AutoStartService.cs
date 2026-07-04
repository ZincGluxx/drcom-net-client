using System;
using System.IO;
using Microsoft.Win32;

namespace CampusNetworkLogin.Services;

/// <summary>
/// 开机启动管理服务
/// </summary>
public class AutoStartService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CampusNetworkLogin";

    /// <summary>
    /// 检查是否已设置开机启动
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            if (key == null) return false;
            var value = key.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value) &&
                   Unquote(value).Equals(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置或取消开机启动
    /// </summary>
    public void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var executablePath = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(executablePath))
                    throw new InvalidOperationException("无法获取当前程序路径");

                key.SetValue(AppName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"设置开机启动失败: {ex.Message}", ex);
        }
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }

    private static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Replace(".dll", ".exe", StringComparison.OrdinalIgnoreCase);
        }
        return path ?? "";
    }
}
