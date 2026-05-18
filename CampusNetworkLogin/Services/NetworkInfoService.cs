using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace CampusNetworkLogin.Services;

/// <summary>
/// 获取本机网络信息（IP、MAC、主机名等）
/// </summary>
public class NetworkInfoService
{
    /// <summary>
    /// 获取首选IPv4地址
    /// </summary>
    public static string GetLocalIpAddress()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var iface in interfaces)
            {
                var ipInfo = iface.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                
                if (ipInfo != null)
                {
                    return ipInfo.Address.ToString();
                }
            }
            return "0.0.0.0";
        }
        catch
        {
            return "0.0.0.0";
        }
    }

    /// <summary>
    /// 获取物理MAC地址（格式: 0x开头十六进制）
    /// </summary>
    public static string GetMacAddress()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var nic in nics)
            {
                var addr = nic.GetPhysicalAddress().GetAddressBytes();
                if (addr.Length == 6 && addr.Any(b => b != 0))
                {
                    var hex = BitConverter.ToString(addr).Replace("-", "");
                    return "0x" + hex.ToLower();
                }
            }
        }
        catch { }

        return "0x888888888888";
    }

    /// <summary>
    /// 获取计算机名
    /// </summary>
    public static string GetHostName()
    {
        return Environment.MachineName;
    }

    /// <summary>
    /// 获取操作系统版本
    /// </summary>
    public static string GetOsVersion()
    {
        return $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
    }
}
