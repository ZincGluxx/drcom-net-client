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
            return !string.IsNullOrEmpty(value) && value.Equals(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
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
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                key.SetValue(AppName, GetExecutablePath(), RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"设置开机启动失败: {ex.Message}");
        }
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
