using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace DrComCampus.Services;

internal sealed record NetworkSnapshot(
    string Ipv4Address,
    string MacAddress,
    string AdapterName,
    string SubnetMask,
    string Gateway,
    string DnsServer,
    bool IsReady,
    string Ipv6 = "--",
    string Ipv6Gateway = "--",
    string Ipv6Dns = "--",
    bool HasGlobalIpv6 = false);

/// <summary>
/// 获取本机网络信息（IP、MAC、主机名等）
/// </summary>
internal static class NetworkInfoService
{
    private const string DefaultIp = "0.0.0.0";
    private const string DefaultMac = "0x888888888888";

    // 短时缓存，避免高频调用反复扫描网卡
    private sealed record CacheEntry(NetworkSnapshot Snapshot, long ExpiresAt);

    private static readonly Lock s_cacheSync = new();
    private static CacheEntry? s_cache;
    private const long CacheDurationMs = 3000;

    /// <summary>
    /// 获取本机网络快照（3 秒内返回缓存；force=true 强制重新扫描）
    /// </summary>
    public static NetworkSnapshot GetNetworkSnapshot(bool force = false)
    {
        var now = Environment.TickCount64;
        var cached = Volatile.Read(ref s_cache);
        if (!force && cached != null && now < cached.ExpiresAt)
        {
            return cached.Snapshot;
        }

        lock (s_cacheSync)
        {
            now = Environment.TickCount64;
            cached = s_cache;
            if (!force && cached != null && now < cached.ExpiresAt)
            {
                return cached.Snapshot;
            }

            NetworkSnapshot result;
            try
            {
                result = SelectPreferredSnapshot() ??
                    new NetworkSnapshot(DefaultIp, DefaultMac, "--", "255.255.255.0", "--", "--", false);
            }
            catch
            {
                result = new NetworkSnapshot(DefaultIp, DefaultMac, "--", "255.255.255.0", "--", "--", false);
            }

            Volatile.Write(ref s_cache, new CacheEntry(result, now + CacheDurationMs));
            return result;
        }
    }

    public static void InvalidateCache() => Volatile.Write(ref s_cache, null);

    private static NetworkSnapshot? SelectPreferredSnapshot()
    {
        NetworkSnapshot? best = null;
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var candidate = CreateSnapshot(networkInterface);
            if (candidate.Ipv4Address == DefaultIp && candidate.MacAddress == DefaultMac)
            {
                continue;
            }

            if (best == null || (!best.IsReady && candidate.IsReady))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static NetworkSnapshot CreateSnapshot(NetworkInterface networkInterface)
    {
        var properties = networkInterface.GetIPProperties();
        var ipv4Information = properties.UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .FirstOrDefault(a => IsUsableIpv4(a.Address));
        var ipv4Address = ipv4Information?.Address.ToString() ?? DefaultIp;
        var subnetMask = ipv4Information?.IPv4Mask?.ToString() ?? "255.255.255.0";

        var macBytes = networkInterface.GetPhysicalAddress().GetAddressBytes();
        var macAddress = DefaultMac;
        if (macBytes.Length == 6 && macBytes.Any(b => b != 0))
        {
            var hex = Convert.ToHexString(macBytes);
            macAddress = "0x" + hex;
        }

        var gateway = properties.GatewayAddresses
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .FirstOrDefault() ?? "--";

        var dnsServer = properties.DnsAddresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .FirstOrDefault() ?? "--";

        var ipv6Address = properties.UnicastAddresses
            .Select(a => a.Address)
            .FirstOrDefault(IsGlobalIpv6);
        var ipv6 = ipv6Address?.ToString() ?? "--";

        var ipv6Gateway = properties.GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 !a.Equals(IPAddress.IPv6Any))
            ?.ToString() ?? "--";

        var ipv6Dns = properties.DnsAddresses
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
            ?.ToString() ?? "--";

        var isReady = ipv4Address != DefaultIp && macAddress != DefaultMac;
        return new NetworkSnapshot(ipv4Address, macAddress, networkInterface.Name, subnetMask, gateway, dnsServer, isReady,
            ipv6, ipv6Gateway, ipv6Dns, ipv6Address != null);
    }

    private static bool IsUsableIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
               bytes[0] != 127 &&
               !(bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsGlobalIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal ||
            address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 16 && (bytes[0] & 0xE0) == 0x20;
    }

    public static string OsVersion =>
        $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
}
