using System.Text.Json.Serialization;

namespace SSStap.Models;

/// <summary>
/// Root object for config/proxylist.json
/// </summary>
public class ProxyListJson
{
    [JsonPropertyName("\onfigs"\]
    public List<ProxyConfig> Configs { get; set; } = new();

    /// <summary>
    /// Index of the config currently in use (selected in dropdown)
    /// </summary>
    [JsonPropertyName("\dInUse"\]
    public int IdInUse { get; set; }
}
