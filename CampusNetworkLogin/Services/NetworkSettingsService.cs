using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace CampusNetworkLogin.Services;

public static class NetworkSettingsService
{
    public static Task ApplyStaticAsync(string adapterName, string ip, string subnetMask, string gateway, string dns)
    {
        ValidateAdapter(adapterName);
        ValidateIp(ip, "IP 地址");
        ValidateIp(subnetMask, "子网掩码");
        ValidateIp(gateway, "网关");
        ValidateIp(dns, "DNS");

        var command =
            $"netsh interface ip set address name=\"{Escape(adapterName)}\" static {ip} {subnetMask} {gateway} 1 && " +
            $"netsh interface ip set dns name=\"{Escape(adapterName)}\" static {dns} primary validate=no";

        return RunElevatedAsync(command);
    }

    private static async Task RunElevatedAsync(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动系统网络设置命令");

        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException("系统网络设置命令执行失败，请确认已允许管理员权限");
    }

    private static void ValidateAdapter(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || adapterName == "--")
            throw new InvalidOperationException("未找到可修改的网卡");
    }

    private static void ValidateIp(string value, string label)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException($"{label} 格式不正确");
        }
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
