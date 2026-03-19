using System.Text.Json.Serialization;

namespace SSStap.Models;

/// <summary>
/// Single proxy configuration matching proxylist.json schema.
/// Type codes: 1=HTTP, 4=SOCKS4, 5=SOCKS5, 6=Shadowsocks, 7=ShadowsocksR
/// </summary>
public class ProxyConfig
{
    [JsonPropertyName("\d"\]
    public string? Id { get; set; }

    [JsonPropertyName("\erver"\]
    public string Server { get; set; } = "\