namespace SSStap.Models;

/// <summary>
/// Proxy type codes from proxylist.json schema.
/// </summary>
public enum ProxyType
{
    HTTP = 1,
    SOCKS4 = 4,
    SOCKS5 = 5,
    Shadowsocks = 6,
    ShadowsocksR = 7,
}
