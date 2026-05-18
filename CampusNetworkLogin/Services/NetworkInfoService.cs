using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace CampusNetworkLogin.Services;

/// <summary>
/// 获取本机网络信息（IP、MAC、主机名等）
/// </summary>
public class NetworkInfoService
{
    /// <summary>
    /// 单次扫描网卡，同时返回首选 IPv4 与 MAC
    /// </summary>
    public static (string Ip, string Mac) GetNetworkInfo()
    {
        const string defaultIp = "0.0.0.0";
        const string defaultMac = "0x888888888888";

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            string? ip = null;
            string? mac = null;

            foreach (var iface in interfaces)
            {
                if (ip == null)
                {
                    var ipInfo = iface.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipInfo != null)
                        ip = ipInfo.Address.ToString();
                }

                if (mac == null)
                {
                    var addr = iface.GetPhysicalAddress().GetAddressBytes();
                    if (addr.Length == 6 && addr.Any(b => b != 0))
                    {
                        var hex = BitConverter.ToString(addr).Replace("-", "");
                        mac = "0x" + hex.ToLower();
                    }
                }

                if (ip != null && mac != null)
                    break;
            }

            return (ip ?? defaultIp, mac ?? defaultMac);
        }
        catch
        {
            return (defaultIp, defaultMac);
        }
    }

    public static string GetLocalIpAddress() => GetNetworkInfo().Ip;

    public static string GetMacAddress() => GetNetworkInfo().Mac;

    public static string GetHostName() => Environment.MachineName;

    public static string GetOsVersion() =>
        $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
}
