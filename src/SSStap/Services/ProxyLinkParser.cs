using System.Text;
using SSStap.Models;

namespace SSStap.Services;

/// <summary>
/// Parses ss:// and ssr:// links into ProxyConfig.
/// </summary>
public static class ProxyLinkParser
{
    public static ProxyConfig? Parse(string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        link = link.Trim();

        if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
            return ParseSs(link);
        if (link.StartsWith("ssr://", StringComparison.OrdinalIgnoreCase))
            return ParseSsr(link);

        return null;
    }

    private static ProxyConfig? ParseSs(string link)
    {
        // ss://base64(method:password)@host:port or ss://base64(method:password)@host:port#tag
        // SIP002: ss://userinfo@host:port#tag where userinfo = base64url(method:password)
        try
        {
            var rest = link["ss://".Length..];
            string? tag = null;
            var hashIdx = rest.IndexOf('#');
            if (hashIdx >= 0)
            {
                tag = Uri.UnescapeDataString(rest[(hashIdx + 1)..]);
                rest = rest[..hashIdx];
            }

            string host;
            int port;
            string method;
            string password;

            var atIdx = rest.IndexOf('@');
            if (atIdx >= 0)
            {
                var userInfo = rest[..atIdx];
                var hostPort = rest[(atIdx + 1)..];
                var decoded = DecodeBase64Url(userInfo) ?? DecodeBase64(userInfo);
                if (decoded == null) return null;
                var colon = decoded.IndexOf(':');
                if (colon < 0) return null;
                method = decoded[..colon];
                password = decoded[(colon + 1)..];
                var (h, p) = ParseHostPort(hostPort);
                host = h;
                port = p;
            }
            else
            {
                var decoded = DecodeBase64Url(rest) ?? DecodeBase64(rest);
                if (decoded == null) return null;
                var at = decoded.IndexOf('@');
                if (at < 0) return null;
                var left = decoded[..at];
                var right = decoded[(at + 1)..];
                var colon = left.IndexOf(':');
                if (colon < 0) return null;
                method = left[..colon];
                password = left[(colon + 1)..];
                var (h, p) = ParseHostPort(right);
                host = h;
                port = p;
            }

            return new ProxyConfig
            {
                Server = host,
                ServerPort = port,
                Password = password,
                Method = method,
                Remarks = tag ?? "",
                Group = "Default Group",
                Type = (int)ProxyType.Shadowsocks,
                Protocol = "origin",
                Obfs = "plain",
            };
        }
        catch
        {
            return null;
        }
    }

    private static ProxyConfig? ParseSsr(string link)
    {
        try
        {
            var rest = link["ssr://".Length..];
            var decoded = DecodeBase64Url(rest) ?? DecodeBase64(rest);
            if (decoded == null) return null;

            string? obfsparam = null, protoparam = null, remarks = null, group = null;
            var slashIdx = decoded.IndexOf("/?");
            var main = decoded;
            if (slashIdx >= 0)
            {
                main = decoded[..slashIdx];
                var paramStr = decoded[(slashIdx + 2)..];
                foreach (var pair in paramStr.Split('&'))
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = pair[..eq].ToLowerInvariant();
                    var val = pair[(eq + 1)..];
                    var decodedVal = DecodeBase64Url(val) ?? DecodeBase64(val) ?? val;
                    switch (key)
                    {
                        case "obfsparam": obfsparam = decodedVal; break;
                        case "protoparam": protoparam = decodedVal; break;
                        case "remarks": remarks = decodedVal; break;
                        case "group": group = decodedVal; break;
                    }
                }
            }

            var parts = main.Split(':');
            if (parts.Length < 6) return null;

            var host = parts[0];
            if (!int.TryParse(parts[1], out var port)) return null;
            var protocol = parts[2];
            var method = parts[3];
            var obfs = parts[4];
            var password = DecodeBase64Url(parts[5]) ?? DecodeBase64(parts[5]) ?? parts[5];

            return new ProxyConfig
            {
                Server = host,
                ServerPort = port,
                Password = password,
                Method = method,
                Protocol = protocol,
                Obfs = obfs,
                ObfsParam = obfsparam ?? "",
                ProtocolParam = protoparam ?? "",
                Remarks = remarks ?? "",
                Group = group ?? "Default Group",
                Type = (int)ProxyType.ShadowsocksR,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        var pad = (4 - s.Length % 4) % 4;
        s += new string('=', pad);
        return DecodeBase64(s);
    }

    private static string? DecodeBase64(string s)
    {
        try
        {
            var bytes = Convert.FromBase64String(s);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static (string host, int port) ParseHostPort(string hostPort)
    {
        var lastColon = hostPort.LastIndexOf(':');
        if (lastColon < 0) return (hostPort, 8388);
        var host = hostPort[..lastColon];
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']');
            if (end >= 0) host = host[1..end];
        }
        var portStr = hostPort[(lastColon + 1)..];
        var port = int.TryParse(portStr, out var p) ? p : 8388;
        return (host, port);
    }
}
