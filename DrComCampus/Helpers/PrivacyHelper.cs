using System;

namespace DrComCampus.Helpers;

/// <summary>
/// 登录页展示用 IP/MAC 脱敏
/// </summary>
internal static class PrivacyHelper
{
    public static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return "--";
        }

        // IPv6：保留前两段，掩码其余部分
        if (ip.Contains(':', StringComparison.Ordinal))
        {
            var parts = ip.Split(':');
            if (parts.Length >= 2)
            {
                return $"{parts[0]}:{parts[1]}::/64";
            }

            return "--";
        }

        var parts4 = ip.Split('.');
        if (parts4.Length == 4)
        {
            return $"{parts4[0]}.{parts4[1]}.*.*";
        }

        return "--";
    }

    public static string MaskMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac == "0x888888888888")
        {
            return "--";
        }

        var hex = mac.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? mac[2..]
            : mac;

        if (hex.Length < 6)
        {
            return "--";
        }

        return $"0x{hex[..2]}******{hex[^2..]}";
    }
}
