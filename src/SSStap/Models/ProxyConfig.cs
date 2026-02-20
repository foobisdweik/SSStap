using System.Text.Json.Serialization;

namespace SSStap.Models;

/// <summary>
/// Single proxy configuration matching proxylist.json schema.
/// Type codes: 1=HTTP, 4=SOCKS4, 5=SOCKS5, 6=Shadowsocks, 7=ShadowsocksR
/// </summary>
public class ProxyConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("server")]
    public string Server { get; set; } = "";

    [JsonPropertyName("server_port")]
    public int ServerPort { get; set; }

    [JsonPropertyName("server_udp_port")]
    public int ServerUdpPort { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "none";

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "origin";

    [JsonPropertyName("obfs")]
    public string Obfs { get; set; } = "plain";

    [JsonPropertyName("obfsparam")]
    public string ObfsParam { get; set; } = "";

    [JsonPropertyName("protocolparam")]
    public string ProtocolParam { get; set; } = "";

    [JsonPropertyName("remarks")]
    public string Remarks { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "Default Group";

    [JsonPropertyName("type")]
    public int Type { get; set; } = 5; // 5 = SOCKS5

    [JsonPropertyName("enable_quic")]
    public bool EnableQuic { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("AdditionalRoute")]
    public string AdditionalRoute { get; set; } = "";

    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("ConnectedTimes")]
    public int ConnectedTimes { get; set; }

    [JsonPropertyName("Download_traffic")]
    public long DownloadTraffic { get; set; }

    [JsonPropertyName("FailureTimes")]
    public int FailureTimes { get; set; }

    [JsonPropertyName("Latency")]
    public int Latency { get; set; }

    [JsonPropertyName("Upload_traffic")]
    public long UploadTraffic { get; set; }

    [JsonPropertyName("charge_type")]
    public int ChargeType { get; set; }

    [JsonPropertyName("iplocation")]
    public string IpLocation { get; set; } = "";

    [JsonPropertyName("ipregion")]
    public string IpRegion { get; set; } = "";

    [JsonPropertyName("nCountryIndex")]
    public int CountryIndex { get; set; } = -1;

    [JsonPropertyName("udp_over_tcp")]
    public int UdpOverTcp { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Remarks) ? $"{Server}:{ServerPort}" : Remarks;

    /// <summary>Returns true when the minimum required fields are populated with valid values.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Server) &&
        ServerPort >= 1 &&
        ServerPort <= 65535;
}
