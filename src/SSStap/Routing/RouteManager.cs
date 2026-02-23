using System.IO;
using System.Net;

namespace SSStap.Routing;

/// <summary>
/// Manages system routes for SSStap based on routing mode and Wintun interface.
/// Applies/removes routes on connect/disconnect.
/// </summary>
public sealed class RouteManager
{
    /// <summary>
    /// Route metric for proxy routes. Lower = higher preference.
    /// Use 1 to override the default route so traffic goes via Wintun.
    /// </summary>
    public const uint DefaultRouteMetric = 1;

    private readonly string _rulesBasePath;
    private ChinaIpRules? _chinaRules;
    private RoutingMode _currentMode;
    private readonly List<(IPAddress Dest, int Prefix, uint IfIndex)> _addedRoutes = new();
    private readonly object _lock = new();

    public RouteManager(string? rulesBasePath = null)
    {
        _rulesBasePath = rulesBasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules");
    }

    /// <summary>
    /// Applies routes for the given mode. Call on Connect.
    /// </summary>
    /// <param name="mode">Routing mode (Global, ChinaOnly, SkipChina).</param>
    /// <param name="wintunInterfaceIndex">Wintun adapter's interface index.</param>
    /// <param name="physicalAdapterIndex">
    /// Index of the physical (hotspot) adapter. Required when installing any default
    /// route (Global, SkipChina, or ChinaIpRules-empty fallback): a host route for
    /// <paramref name="proxyServerAddress"/> is installed on this adapter at metric 0
    /// BEFORE the Wintun default route. Without this, traffic to the proxy server is
    /// sent into Wintun, which forwards it back to the proxy, causing an infinite loop.
    /// </param>
    /// <param name="proxyServerAddress">
    /// IP address of the SOCKS5 proxy server (e.g. 172.20.10.1). A /32 host route for
    /// this address is installed on <paramref name="physicalAdapterIndex"/> ahead of any
    /// default route. Ignored if null or if <paramref name="physicalAdapterIndex"/> is null.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> ApplyRoutesAsync(
        RoutingMode mode,
        uint wintunInterfaceIndex,
        uint? physicalAdapterIndex = null,
        IPAddress? proxyServerAddress = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_addedRoutes.Count > 0)
                return false;
        }

        if (mode == RoutingMode.BrowserOnly)
            return true;

        _currentMode = mode;

        if (mode == RoutingMode.Global)
        {
            TryAddProxyHostRoute(proxyServerAddress, physicalAdapterIndex);
            var err = RouteTableApi.AddRoute(IPAddress.Any, 0, wintunInterfaceIndex, DefaultRouteMetric);
            if (err != 0) return false;
            lock (_lock) { _addedRoutes.Add((IPAddress.Any, 0, wintunInterfaceIndex)); }
            return true;
        }

        var rulesPath = Path.Combine(_rulesBasePath,
            mode == RoutingMode.ChinaOnly ? "China-IP-only.rules" : "Skip-all-China-IP.rules");
        _chinaRules = await ChinaIpRules.LoadFromFileAsync(rulesPath, ct);

        if (_chinaRules.Count == 0)
        {
            TryAddProxyHostRoute(proxyServerAddress, physicalAdapterIndex);
            var err = RouteTableApi.AddRoute(IPAddress.Any, 0, wintunInterfaceIndex, DefaultRouteMetric);
            if (err != 0) return false;
            lock (_lock) { _addedRoutes.Add((IPAddress.Any, 0, wintunInterfaceIndex)); }
            return true;
        }

        if (mode == RoutingMode.ChinaOnly)
        {
            foreach (var (network, prefixLength) in _chinaRules.GetCidrs())
            {
                var addr = UintToIpAddress(network);
                var err = RouteTableApi.AddRoute(addr, prefixLength, wintunInterfaceIndex, DefaultRouteMetric);
                if (err == 0)
                    lock (_lock) { _addedRoutes.Add((addr, prefixLength, wintunInterfaceIndex)); }
                if (ct.IsCancellationRequested) break;
            }
            return true;
        }

        // SkipChina: add China routes to physical (highest priority), then default to Wintun.
        // Proxy host route is added before the Wintun default to prevent the routing loop.
        if (mode == RoutingMode.SkipChina && physicalAdapterIndex.HasValue)
        {
            foreach (var (network, prefixLength) in _chinaRules.GetCidrs())
            {
                var addr = UintToIpAddress(network);
                var err = RouteTableApi.AddRoute(addr, prefixLength, physicalAdapterIndex.Value, 0);
                if (err == 0)
                    lock (_lock) { _addedRoutes.Add((addr, prefixLength, physicalAdapterIndex.Value)); }
                if (ct.IsCancellationRequested) break;
            }
            TryAddProxyHostRoute(proxyServerAddress, physicalAdapterIndex);
            var err2 = RouteTableApi.AddRoute(IPAddress.Any, 0, wintunInterfaceIndex, DefaultRouteMetric);
            if (err2 != 0) return false;
            lock (_lock) { _addedRoutes.Add((IPAddress.Any, 0, wintunInterfaceIndex)); }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Installs a /32 host route for the proxy server on the physical adapter at metric 0.
    /// Must be called before any default route (0.0.0.0/0) is added to Wintun, otherwise
    /// traffic to the proxy server routes into Wintun and creates an infinite relay loop.
    /// No-ops silently if either argument is null.
    /// </summary>
    private void TryAddProxyHostRoute(IPAddress? proxyServerAddress, uint? physicalAdapterIndex)
    {
        if (proxyServerAddress == null || !physicalAdapterIndex.HasValue)
            return;

        var err = RouteTableApi.AddRoute(proxyServerAddress, 32, physicalAdapterIndex.Value, 0);
        if (err == 0)
            lock (_lock) { _addedRoutes.Add((proxyServerAddress, 32, physicalAdapterIndex.Value)); }
    }

    private static IPAddress UintToIpAddress(uint network)
    {
        var b = BitConverter.GetBytes(network);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return new IPAddress(b);
    }

    /// <summary>Removes all routes added by ApplyRoutes. Call on Disconnect.</summary>
    public void RemoveAllRoutes()
    {
        lock (_lock)
        {
            foreach (var (dest, prefix, ifIndex) in _addedRoutes)
            {
                RouteTableApi.RemoveRoute(dest, prefix, ifIndex);
            }
            _addedRoutes.Clear();
        }
    }

    /// <summary>Returns true if the destination should be forwarded via proxy (for packet-level filtering).</summary>
    public bool ShouldForwardViaProxy(IPAddress destination)
    {
        if (_chinaRules == null) return true;
        var inChina = _chinaRules.Contains(destination);
        return _currentMode == RoutingMode.ChinaOnly ? inChina : !inChina;
    }
}
