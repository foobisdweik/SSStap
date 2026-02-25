using System.Net;
using System.Runtime.InteropServices;

namespace SSStap.Routing;

/// <summary>
/// P/Invoke to iphlpapi.dll for route table manipulation.
/// Uses CreateIpForwardEntry2 / DeleteIpForwardEntry2 (Vista+).
/// Requires admin privileges.
/// </summary>
public static class RouteTableApi
{
    private const uint AF_INET = 2;
    private const uint MIB_IPPROTO_NETMGMT = 3;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint CreateIpForwardEntry2(ref MibIpForwardRow2 row);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint DeleteIpForwardEntry2(ref MibIpForwardRow2 row);

    public static uint AddRoute(IPAddress destination, int prefixLength, uint interfaceIndex, uint metric = 1)
    {
        var row = BuildRow(destination, prefixLength, interfaceIndex, metric);
        return CreateIpForwardEntry2(ref row);
    }

    public static uint RemoveRoute(IPAddress destination, int prefixLength, uint interfaceIndex)
    {
        var row = BuildRow(destination, prefixLength, interfaceIndex, 0);
        return DeleteIpForwardEntry2(ref row);
    }

    private static MibIpForwardRow2 BuildRow(IPAddress destination, int prefixLength, uint interfaceIndex, uint metric)
    {
        var bytes = destination.GetAddressBytes();
        if (bytes.Length != 4) throw new ArgumentException("IPv4 only", nameof(destination));

        var row = new MibIpForwardRow2();

        // InterfaceLuid = 0, InterfaceIndex drives lookup
        row.InterfaceIndex = interfaceIndex;

        // DestinationPrefix (native IP_ADDRESS_PREFIX, 32 bytes at offset 12):
        //   SOCKADDR_INET Prefix (28 bytes): sin_family at [0], sin_addr at [4]
        //   UINT8 PrefixLength (1 byte at offset 28), 3 bytes natural padding
        row.DestPrefixFamily   = (ushort)AF_INET;
        row.DestPrefixAddrB0   = bytes[0];
        row.DestPrefixAddrB1   = bytes[1];
        row.DestPrefixAddrB2   = bytes[2];
        row.DestPrefixAddrB3   = bytes[3];
        row.DestPrefixLength   = (byte)prefixLength;

        // NextHop (native SOCKADDR_INET, 28 bytes at offset 44): 0.0.0.0 = on-link
        row.NextHopFamily      = (ushort)AF_INET;

        row.ValidLifetime      = 0xFFFFFFFF;
        row.PreferredLifetime  = 0xFFFFFFFF;
        row.Metric             = metric;
        row.Protocol           = MIB_IPPROTO_NETMGMT;
        row.Immortal           = 1;

        return row;
    }

    // MIB_IPFORWARD_ROW2 — explicit layout, all offsets verified against netioapi.h
    // (Windows SDK 10.0.26100), x64, total 104 bytes.
    //
    //  [0]   NET_LUID  InterfaceLuid         8
    //  [8]   DWORD     InterfaceIndex        4
    //  [12]  IP_ADDRESS_PREFIX               32   ← was 36 in LayoutKind.Sequential due to
    //         [12] SOCKADDR_INET Prefix      28     extra _pad3 uint; each field below was
    //         [40] UINT8 PrefixLength         1     4 bytes past its real native offset.
    //         [41] (3 bytes natural padding)
    //  [44]  SOCKADDR_INET NextHop           28
    //  [72]  UINT8  SitePrefixLength          1
    //  [73]  (3 bytes natural padding)
    //  [76]  DWORD  ValidLifetime             4
    //  [80]  DWORD  PreferredLifetime         4
    //  [84]  DWORD  Metric                    4
    //  [88]  DWORD  Protocol                  4
    //  [92]  BOOL   Loopback                  1
    //  [93]  BOOL   AutoconfigureAddress      1
    //  [94]  BOOL   Publish                   1
    //  [95]  BOOL   Immortal                  1
    //  [96]  DWORD  Age                       4
    //  [100] DWORD  Origin                    4
    //                                       104
    [StructLayout(LayoutKind.Explicit, Size = 104)]
    private struct MibIpForwardRow2
    {
        [FieldOffset(0)]  public ulong  InterfaceLuid;
        [FieldOffset(8)]  public uint   InterfaceIndex;

        // DestinationPrefix.Prefix (SOCKADDR_INET at offset 12)
        [FieldOffset(12)] public ushort DestPrefixFamily;    // sin_family
        // sin_port at [14] = 0 (implicit)
        [FieldOffset(16)] public byte   DestPrefixAddrB0;    // sin_addr byte 0
        [FieldOffset(17)] public byte   DestPrefixAddrB1;
        [FieldOffset(18)] public byte   DestPrefixAddrB2;
        [FieldOffset(19)] public byte   DestPrefixAddrB3;
        // DestinationPrefix.PrefixLength at [40]
        [FieldOffset(40)] public byte   DestPrefixLength;
        // [41..43] natural padding

        // NextHop (SOCKADDR_INET at offset 44)
        [FieldOffset(44)] public ushort NextHopFamily;       // sin_family
        // NextHop sin_addr = 0.0.0.0 (on-link, left as default zero)

        [FieldOffset(72)] public byte   SitePrefixLength;
        // [73..75] natural padding
        [FieldOffset(76)] public uint   ValidLifetime;
        [FieldOffset(80)] public uint   PreferredLifetime;
        [FieldOffset(84)] public uint   Metric;
        [FieldOffset(88)] public uint   Protocol;
        [FieldOffset(92)] public byte   Loopback;
        [FieldOffset(93)] public byte   AutoconfigureAddress;
        [FieldOffset(94)] public byte   Publish;
        [FieldOffset(95)] public byte   Immortal;
        [FieldOffset(96)] public uint   Age;
        [FieldOffset(100)] public uint  Origin;
    }
}
