// Adapter IP configuration via iphlpapi.dll
// Uses AddIPAddress/DeleteIPAddress for non-persistent config (cleaned on disconnect)

using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SSStap.Native;

/// <summary>
/// IP configuration for a network adapter identified by NET_LUID or adapter name.
/// Uses iphlpapi.dll: ConvertInterfaceLuidToIndex, AddIPAddress, DeleteIPAddress.
/// </summary>
public static partial class AdapterSetup
{
    private const string DllName = "iphlpapi";

    /// <summary>
    /// Adds an IPv4 address to the adapter. Non-persistent (cleared on adapter reset).
    /// Requires administrator privileges.
    /// </summary>
    /// <param name="address">IPv4 address (e.g. 10.10.10.1).</param>
    /// <param name="subnetMask">Subnet mask (e.g. 255.255.255.0).</param>
    /// <param name="luid">Adapter LUID from WintunGetAdapterLuid.</param>
    /// <returns>Context for DeleteIPAddress, or null on failure.</returns>
    public static IpAddressContext? SetAdapterIp(IPAddress address, IPAddress subnetMask, Wintun.NetLuid luid)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            subnetMask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        // GetAddressBytes() returns bytes in network (big-endian) order, e.g. 10.10.10.1 → [10,10,10,1].
        // BitConverter.ToUInt32 on a little-endian machine produces a uint whose in-memory layout
        // is [10,10,10,1] — exactly what AddIPAddress (Win32 DWORD IPAddr) expects.
        // The previous code built a big-endian *value* (0x0A0A0A01) which Win32 read as 1.10.10.10.
        uint addr = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        uint mask = BitConverter.ToUInt32(subnetMask.GetAddressBytes(), 0);

        if (!ConvertInterfaceLuidToIndex(ref luid, out uint ifIndex))
            return null;

        uint ret = AddIPAddress(addr, mask, ifIndex, out uint nteContext, out uint nteInstance);
        if (ret != 0) // NO_ERROR = 0
            return null;

        return new IpAddressContext(nteContext);
    }

    /// <summary>
    /// Adds an IPv4 address to the adapter by friendly name/description.
    /// Fallback when WintunGetAdapterLuid is unavailable (old wintun.dll).
    /// </summary>
    /// <param name="address">IPv4 address (e.g. 10.10.10.1).</param>
    /// <param name="subnetMask">Subnet mask (e.g. 255.255.255.0).</param>
    /// <param name="adapterName">Adapter name (e.g. "SSStap"). Matches Name or Description.</param>
    /// <returns>Context for DeleteIPAddress, or null on failure.</returns>
    public static IpAddressContext? SetAdapterIp(IPAddress address, IPAddress subnetMask, string adapterName)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            subnetMask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            string.IsNullOrWhiteSpace(adapterName))
            return null;

        uint? ifIndex = GetInterfaceIndexByAdapterName(adapterName);
        if (ifIndex == null)
            return null;

        // Same byte-order fix as the LUID overload.
        uint addr = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        uint mask = BitConverter.ToUInt32(subnetMask.GetAddressBytes(), 0);

        uint ret = AddIPAddress(addr, mask, ifIndex.Value, out uint nteContext, out uint nteInstance);
        if (ret != 0)
            return null;

        return new IpAddressContext(nteContext);
    }

    /// <summary>
    /// Finds the IPv4 interface index for an adapter whose Name or Description contains <paramref name="adapterName"/>.
    /// </summary>
    public static uint? GetInterfaceIndexByAdapterName(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return null;

        var name = adapterName.Trim();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipv4 = ni.GetIPProperties()?.GetIPv4Properties();
            if (ipv4 == null)
                continue;

            if ((ni.Name != null && ni.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
                (ni.Description != null && ni.Description.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return (uint)ipv4.Index;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the interface index of the first active physical adapter that has a default IPv4 gateway.
    /// Used by SkipChina routing to identify which adapter carries non-proxied traffic.
    /// </summary>
    public static uint? GetDefaultGatewayInterfaceIndex()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var props = ni.GetIPProperties();
            if (props?.GatewayAddresses == null)
                continue;

            foreach (var gw in props.GatewayAddresses)
            {
                if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !gw.Address.Equals(IPAddress.Any))
                {
                    var ipv4 = props.GetIPv4Properties();
                    if (ipv4 != null)
                        return (uint)ipv4.Index;
                }
            }
        }
        return null;
    }

    public readonly struct IpAddressContext
    {
        public readonly uint NTEContext;
        public IpAddressContext(uint nteContext) => NTEContext = nteContext;

        public void Delete()
        {
            if (NTEContext != 0)
                _ = DeleteIPAddress(NTEContext);
        }
    }

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertInterfaceLuidToIndex(ref Wintun.NetLuid interfaceLuid, out uint interfaceIndex);

    [LibraryImport(DllName)]
    private static partial uint AddIPAddress(uint address, uint ipMask, uint ifIndex,
        out uint nteContext, out uint nteInstance);

    [LibraryImport(DllName)]
    private static partial uint DeleteIPAddress(uint nteContext);
}
