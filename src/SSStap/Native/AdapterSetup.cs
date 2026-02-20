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

        byte[] addrBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();
        // IPAddr is in network byte order (big-endian): high octet first
        uint addr = (uint)((addrBytes[0] << 24) | (addrBytes[1] << 16) | (addrBytes[2] << 8) | addrBytes[3]);
        uint mask = (uint)((maskBytes[0] << 24) | (maskBytes[1] << 16) | (maskBytes[2] << 8) | maskBytes[3]);

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
    /// <param name="adapterName">Adapter name (e.g. "SSStap" or "SSStap-{guid}"). Matches Name or Description.</param>
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

        byte[] addrBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();
        uint addr = (uint)((addrBytes[0] << 24) | (addrBytes[1] << 16) | (addrBytes[2] << 8) | addrBytes[3]);
        uint mask = (uint)((maskBytes[0] << 24) | (maskBytes[1] << 16) | (maskBytes[2] << 8) | maskBytes[3]);

        uint ret = AddIPAddress(addr, mask, ifIndex.Value, out uint nteContext, out uint nteInstance);
        if (ret != 0)
            return null;

        return new IpAddressContext(nteContext);
    }

    /// <summary>
    /// Finds the IPv4 interface index for an adapter whose Name or Description contains the given string.
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
