using System.Net;
using System.Runtime.InteropServices;

namespace SSStap.Routing;

/// <summary>
/// P/Invoke to iphlpapi.dll for route table manipulation.
/// Uses CreateIpForwardEntry2 / DeleteIpForwardEntry2 (Vista+) for modern API.
/// Requires admin privileges.
/// </summary>
public static class RouteTableApi
{
    private const uint AF_INET = 2;
    private const uint MIB_IPPROTO_NETMGMT = 3; // Static route

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void InitializeIpForwardEntry(ref MibIpForwardRow2 row);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint CreateIpForwardEntry2(ref MibIpForwardRow2 row);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint DeleteIpForwardEntry2(ref MibIpForwardRow2 row);

    /// <summary>Adds a route to send traffic matching the given prefix to the specified interface.</summary>
    /// <param name="destination">Destination prefix (e.g. 0.0.0.0 for default).</param>
    /// <param name="prefixLength">Prefix length in bits (e.g. 0 for 0.0.0.0/0).</param>
    /// <param name="interfaceIndex">Network interface index (e.g. Wintun adapter index).</param>
    /// <param name="metric">Route metric offset (lower = preferred). Use 1 to override default route.</param>
    /// <returns>Win32 error code. 0 = success.</returns>
    public static uint AddRoute(IPAddress destination, int prefixLength, uint interfaceIndex, uint metric = 1)
    {
        MibIpForwardRow2 row = default;
        InitializeIpForwardEntry(ref row);
        row.InterfaceIndex = interfaceIndex;
        row.DestinationPrefix = IpAddressPrefixFrom(destination, prefixLength);
        row.NextHop = SockaddrInetFromIPv4(0, 0, 0, 0); // On-link
        row.SitePrefixLength = 0;
        row.ValidLifetime = 0xFFFFFFFF;
        row.PreferredLifetime = 0xFFFFFFFF;
        row.Metric = metric;
        row.Protocol = MIB_IPPROTO_NETMGMT;
        row.Loopback = 0;
        row.AutoconfigureAddress = 0;
        row.Publish = 0;
        row.Immortal = 1;
        return CreateIpForwardEntry2(ref row);
    }

    /// <summary>Removes a route. Must match the same fields used when adding.</summary>
    public static uint RemoveRoute(IPAddress destination, int prefixLength, uint interfaceIndex)
    {
        MibIpForwardRow2 row = default;
        row.InterfaceIndex = interfaceIndex;
        row.DestinationPrefix = IpAddressPrefixFrom(destination, prefixLength);
        row.NextHop = SockaddrInetFromIPv4(0, 0, 0, 0);
        return DeleteIpForwardEntry2(ref row);
    }

    private static IpAddressPrefix IpAddressPrefixFrom(IPAddress addr, int prefixLength)
    {
        var bytes = addr.GetAddressBytes();
        if (bytes.Length != 4) throw new ArgumentException("IPv4 only supported", nameof(addr));
        return new IpAddressPrefix
        {
            Prefix = SockaddrInetFromIPv4(bytes[0], bytes[1], bytes[2], bytes[3]),
            PrefixLength = (byte)prefixLength,
        };
    }

    private static SockaddrInet SockaddrInetFromIPv4(byte b0, byte b1, byte b2, byte b3)
    {
        return new SockaddrInet
        {
            Ipv4 = new SockaddrIn
            {
                sin_family = (ushort)AF_INET,
                sin_port = 0,
                sin_addr = new InAddr { s_b1 = b0, s_b2 = b1, s_b3 = b2, s_b4 = b3 },
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpForwardRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public IpAddressPrefix DestinationPrefix;
        public SockaddrInet NextHop;
        public byte SitePrefixLength;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public uint Metric;
        public uint Protocol;
        public byte Loopback;
        public byte AutoconfigureAddress;
        public byte Publish;
        public byte Immortal;
        public uint Age;
        public uint Origin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IpAddressPrefix
    {
        public SockaddrInet Prefix;
        public byte PrefixLength;
        private byte _pad1;
        private ushort _pad2;
        private uint _pad3;
    }

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    private struct SockaddrInet
    {
        [FieldOffset(0)] public SockaddrIn Ipv4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SockaddrIn
    {
        public ushort sin_family;
        public ushort sin_port;
        public InAddr sin_addr;
        public byte sin_zero_0, sin_zero_1, sin_zero_2, sin_zero_3, sin_zero_4, sin_zero_5, sin_zero_6, sin_zero_7;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InAddr
    {
        public byte s_b1, s_b2, s_b3, s_b4;
    }
}
