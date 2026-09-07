using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DrComCampus.Services;

internal static class NetworkSettingsService
{
    public static Task ApplyStaticIpv4ConfigurationAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string gateway,
        string dnsServer)
    {
        ValidateAdapterName(adapterName);
        ValidateIpv4Address(ipAddress, "IP 地址");
        ValidateIpv4Address(subnetMask, "子网掩码");
        ValidateIpv4Address(gateway, "网关");
        ValidateIpv4Address(dnsServer, "DNS");

        var encodedAdapterName = Convert.ToBase64String(Encoding.UTF8.GetBytes(adapterName));
        var command = string.Join(
            Environment.NewLine,
            $"$adapter = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedAdapterName}'))",
            $"& netsh.exe interface ipv4 set address name=$adapter static {ipAddress} {subnetMask} {gateway} 1",
            "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }",
            $"& netsh.exe interface ipv4 set dnsservers name=$adapter static {dnsServer} primary validate=no",
            "exit $LASTEXITCODE");

        return RunElevatedAsync(command);
    }

    private static async Task RunElevatedAsync(string command)
    {
        // 使用 PowerShell 隐藏窗口执行，避免 cmd.exe 闪现
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-WindowStyle Hidden -NoProfile -NonInteractive -EncodedCommand {EncodePowerShellCommand(command)}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动系统网络设置命令");

        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("系统网络设置命令执行失败，请确认已允许管理员权限");
        }
    }

    private static void ValidateAdapterName(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || adapterName == "--")
        {
            throw new InvalidOperationException("未找到可修改的网卡");
        }
    }

    private static void ValidateIpv4Address(string value, string label)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException($"{label} 格式不正确");
        }
    }

    private static string EncodePowerShellCommand(string command) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
}
