using System.IO;
using System.Net;

namespace SSStap.Routing;

/// <summary>
/// Loads and queries China IP rules from rules/China-IP-only.rules and rules/Skip-all-China-IP.rules.
/// Both files share the same CIDR list format; the mode determines usage:
/// - ChinaOnly: route through proxy iff destination is in the list
/// - SkipChina: route through proxy iff destination is NOT in the list
/// </summary>
public sealed class ChinaIpRules
{
    private readonly List<(uint Network, byte PrefixLength)> _cidrs = new();

    /// <summary>Number of loaded CIDR entries.</summary>
    public int Count => _cidrs.Count;

    /// <summary>Enumerates all loaded CIDRs as (network uint, prefix length).</summary>
    public IEnumerable<(uint Network, byte PrefixLength)> GetCidrs() => _cidrs;

    /// <summary>
    /// Load rules from a .rules file. Skips comment lines (starting with #).
    /// Each data line is a CIDR (e.g. 1.0.1.0/24).
    /// </summary>
    public static async Task<ChinaIpRules> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        var rules = new ChinaIpRules();
        if (!File.Exists(filePath))
            return rules;

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (TryParseCidr(trimmed, out var network, out var prefixLength))
                rules._cidrs.Add((network, prefixLength));
        }
        return rules;
    }

    /// <summary>Returns true if the given IP is covered by any loaded CIDR.</summary>
    public bool Contains(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return false;
        // GetAddressBytes() returns network byte order (big-endian)
        uint ip = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

        foreach (var (network, prefixLength) in _cidrs)
        {
            if (prefixLength == 0) return true;
            var mask = prefixLength >= 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - prefixLength));
            if ((ip & mask) == (network & mask))
                return true;
        }
        return false;
    }

    private static bool TryParseCidr(string cidr, out uint network, out byte prefixLength)
    {
        network = 0;
        prefixLength = 0;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0].Trim(), out var addr) || addr.GetAddressBytes().Length != 4)
            return false;
        if (!byte.TryParse(parts[1].Trim(), out prefixLength) || prefixLength > 32)
            return false;

        var bytes = addr.GetAddressBytes();
        var raw = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        var mask = prefixLength >= 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - prefixLength));
        network = raw & mask;
        return true;
    }
}
