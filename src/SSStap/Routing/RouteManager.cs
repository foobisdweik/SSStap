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
    /// <param name="physicalAdapterIndex">For SkipChina: index of physical adapter for China routes. Null = SkipChina not supported.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> ApplyRoutesAsync(
        RoutingMode mode,
        uint wintunInterfaceIndex,
        uint? physicalAdapterIndex = null,
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

        // SkipChina: add China routes to physical (highest priority), then default to Wintun
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
            var err2 = RouteTableApi.AddRoute(IPAddress.Any, 0, wintunInterfaceIndex, DefaultRouteMetric);
            if (err2 != 0) return false;
            lock (_lock) { _addedRoutes.Add((IPAddress.Any, 0, wintunInterfaceIndex)); }
            return true;
        }

        return false;
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
