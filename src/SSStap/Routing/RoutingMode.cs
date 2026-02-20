namespace SSStap.Routing;

/// <summary>
/// Routing mode matching the main UI Mode selector.
/// Corresponds to last_proxymode_index in config.ini and bGlobalMode.
/// </summary>
public enum RoutingMode
{
    /// <summary>Route 0.0.0.0/0 through proxy (all traffic).</summary>
    Global = 0,

    /// <summary>Route only China IPs through proxy. Uses China-IP-only.rules.</summary>
    ChinaOnly = 1,

    /// <summary>Route all except China IPs through proxy. Uses Skip-all-China-IP.rules.</summary>
    SkipChina = 2,

    /// <summary>Browser-only (PAC) - no system-wide routing.</summary>
    BrowserOnly = 3,
}
