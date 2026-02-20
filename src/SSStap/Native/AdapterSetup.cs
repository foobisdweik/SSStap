// Adapter IP configuration via iphlpapi.dll
// Uses AddIPAddress/DeleteIPAddress for non-persistent config (cleaned on disconnect)

using System.Net;
using System.Runtime.InteropServices;

namespace SSStap.Native;

/// <summary>
/// IP configuration for a network adapter identified by NET_LUID.
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
