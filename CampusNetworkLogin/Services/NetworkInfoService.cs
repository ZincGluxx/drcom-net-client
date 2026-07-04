using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CampusNetworkLogin.Services;

public sealed record NetworkSnapshot(
    string Ip,
    string Mac,
    string AdapterName,
    string SubnetMask,
    string Gateway,
    string Dns,
    bool IsReady);

/// <summary>
/// 获取本机网络信息（IP、MAC、主机名等）
/// </summary>
public class NetworkInfoService
{
    private const string DefaultIp = "0.0.0.0";
    private const string DefaultMac = "0x888888888888";

    /// <summary>
    /// 单次扫描网卡，同时返回首选 IPv4 与 MAC
    /// </summary>
    public static (string Ip, string Mac) GetNetworkInfo()
    {
        var snapshot = GetNetworkSnapshot();
        return (snapshot.Ip, snapshot.Mac);
    }

    public static NetworkSnapshot GetNetworkSnapshot()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(CreateSnapshot)
                .Where(s => s.Ip != DefaultIp || s.Mac != DefaultMac)
                .OrderByDescending(s => s.IsReady)
                .ThenByDescending(s => s.Gateway != "--")
                .ToList();

            return candidates.FirstOrDefault() ??
                   new NetworkSnapshot(DefaultIp, DefaultMac, "--", "255.255.255.0", "--", "--", false);
        }
        catch
        {
            return new NetworkSnapshot(DefaultIp, DefaultMac, "--", "255.255.255.0", "--", "--", false);
        }
    }

    private static NetworkSnapshot CreateSnapshot(NetworkInterface iface)
    {
        var props = iface.GetIPProperties();
        var ipInfo = props.UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .FirstOrDefault(a => IsUsableIpv4(a.Address));
        var ip = ipInfo?.Address.ToString() ?? DefaultIp;
        var subnetMask = ipInfo?.IPv4Mask?.ToString() ?? "255.255.255.0";

        var macBytes = iface.GetPhysicalAddress().GetAddressBytes();
        var mac = DefaultMac;
        if (macBytes.Length == 6 && macBytes.Any(b => b != 0))
        {
            var hex = BitConverter.ToString(macBytes).Replace("-", "");
            mac = "0x" + hex.ToLowerInvariant();
        }

        var gateway = props.GatewayAddresses
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .FirstOrDefault() ?? "--";

        var dns = props.DnsAddresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .FirstOrDefault() ?? "--";

        var isReady = ip != DefaultIp && mac != DefaultMac;
        return new NetworkSnapshot(ip, mac, iface.Name, subnetMask, gateway, dns, isReady);
    }

    private static bool IsUsableIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
               bytes[0] != 127 &&
               !(bytes[0] == 169 && bytes[1] == 254);
    }

    public static string GetLocalIpAddress() => GetNetworkInfo().Ip;

    public static string GetMacAddress() => GetNetworkInfo().Mac;

    public static string GetHostName() => Environment.MachineName;

    public static string GetOsVersion() =>
        $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
}
