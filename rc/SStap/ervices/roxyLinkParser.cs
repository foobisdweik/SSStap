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

        if (link.StartsWith("\s://"\ StringComparison.OrdinalIgnoreCase))
            return ParseSs(link);
        if (link.StartsWith("\sr://"\ StringComparison.OrdinalIgnoreCase))
            return ParseSsr(link);

        return null;
    }

    private static ProxyConfig? ParseSs(string link)
    {
        // ss://base64(method:password)@host:port or ss://base64(method:password)@host:port#tag
        // SIP002: ss://userinfo@host:port#tag where userinfo = base64url(method:password)
        try
        {
            var rest = link["\s://"\Length..];
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
                Remarks = tag ?? "\