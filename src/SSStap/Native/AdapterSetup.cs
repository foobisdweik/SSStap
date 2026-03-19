// Adapter IP configuration via iphlpapi.dll
// Uses CreateUnicastIpAddressEntry/DeleteUnicastIpAddressEntry (Vista+ API)
// recommended by Wintun documentation for configuring virtual tunnel adapters.

using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SSStap.Native;

public static partial class AdapterSetup
{
    private const string DllName = "iphlpapi";

    // AF_INET / AF_INET6
    private const ushort AfInet  = 2;
    private const ushort AfInet6 = 23;

    // NL_DAD_STATE: IpDadStatePreferred skips duplicate-address detection on a
    // virtual adapter where DAD is meaningless and only adds a 1-second delay.
    private const int DadStatePreferred = 4;

    // Infinite lifetime (0xFFFFFFFF).
    private const uint InfiniteLifetime = uint.MaxValue;

    /// <summary>
    /// MIB_UNICASTIPADDRESS_ROW layout (80 bytes, x64).
    /// Offsets verified against netioapi.h (Windows SDK 10.0.26100).
    ///
    /// [0..27]  SOCKADDR_INET  (union of SOCKADDR_IN[16] and SOCKADDR_IN6[28]; total 28)
    /// [28..31] padding to align NET_LUID to 8 bytes
    /// [32..39] NET_LUID       (ULONG64)
    /// [40..43] NET_IFINDEX    (ULONG)
    /// [44..47] NL_PREFIX_ORIGIN
    /// [48..51] NL_SUFFIX_ORIGIN
    /// [52..55] ValidLifetime
    /// [56..59] PreferredLifetime
    /// [60]     OnLinkPrefixLength
    /// [61]     SkipAsSource (BOOLEAN)
    /// [62..63] padding
    /// [64..67] NL_DAD_STATE
    /// [68..71] SCOPE_ID
    /// [72..79] CreationTimeStamp (LARGE_INTEGER)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    private struct MibUnicastIpAddressRow
    {
        // SOCKADDR_INET — for IPv4 use only the first 8 bytes; rest stays zero.
        [FieldOffset(0)]  public ushort SiFamily;
        [FieldOffset(2)]  public ushort SinPort;        // = 0
        [FieldOffset(4)]  public uint   Ipv4Addr;       // IPv4: raw uint from GetAddressBytes()

        [FieldOffset(32)] public ulong  InterfaceLuid;  // NET_LUID.Value
        [FieldOffset(40)] public uint   InterfaceIndex; // = 0 (resolved from LUID by the kernel)
        [FieldOffset(44)] public int    PrefixOrigin;   // NL_PREFIX_ORIGIN
        [FieldOffset(48)] public int    SuffixOrigin;   // NL_SUFFIX_ORIGIN
        [FieldOffset(52)] public uint   ValidLifetime;
        [FieldOffset(56)] public uint   PreferredLifetime;
        [FieldOffset(60)] public byte   OnLinkPrefixLength;
        [FieldOffset(61)] public byte   SkipAsSource;
        [FieldOffset(64)] public int    DadState;       // NL_DAD_STATE
        [FieldOffset(68)] public uint   ScopeId;
        [FieldOffset(72)] public long   CreationTimeStamp;
    }

    [LibraryImport(DllName)]
    private static partial uint CreateUnicastIpAddressEntry(in MibUnicastIpAddressRow row);

    [LibraryImport(DllName)]
    private static partial uint DeleteUnicastIpAddressEntry(in MibUnicastIpAddressRow row);

    [LibraryImport(DllName)]
    private static partial uint ConvertInterfaceGuidToLuid(ref Guid interfaceGuid, out ulong interfaceLuid);

    /// <summary>
    /// Resolves the interface index directly from a NET_LUID using ConvertInterfaceLuidToIndex.
    /// This is a kernel-level call that succeeds immediately after WintunCreateAdapter returns,
    /// unlike the NetworkInterface managed API which has a registration timing race.
    /// </summary>
    [LibraryImport(DllName)]
    private static partial uint ConvertInterfaceLuidToIndex(ref ulong interfaceLuid, out uint interfaceIndex);

    /// <summary>
    /// Returns the interface index for a Wintun adapter via its NET_LUID.
    /// Preferred over GetInterfaceIndexByAdapterName — no timing race against
    /// the .NET NetworkInterface stack registering the new adapter.
    /// </summary>
    public static uint? GetInterfaceIndexByLuid(Wintun.NetLuid luid)
    {
        if (luid.Value == 0) return null;
        var v = luid.Value;
        uint err = ConvertInterfaceLuidToIndex(ref v, out uint index);
        return err == 0 ? index : (uint?)null;
    }

    /// <summary>
    /// Assigns an IPv4 address to the adapter identified by LUID.
    /// Uses <c>CreateUnicastIpAddressEntry</c> (the API Wintun documentation recommends).
    /// Retries up to 6 times on transient error codes 87/1168 that occur during the
    /// brief window between adapter creation and full IP stack registration.
    /// Requires administrator privileges.
    /// </summary>
    public static IpAddressContext? SetAdapterIp(IPAddress address, IPAddress subnetMask, Wintun.NetLuid luid)
    {
        if (luid.Value == 0)
            return null;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            subnetMask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        // GetAddressBytes() returns network-byte-order bytes (big-endian).
        // BitConverter.ToUInt32 on a little-endian host produces a uint whose
        // in-memory layout is [a,b,c,d] — exactly what the SOCKADDR_IN sin_addr
        // field expects (the field is a network-order DWORD, not a host-order int).
        uint addr = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        byte prefix = MaskToPrefixLength(subnetMask);

        var row = new MibUnicastIpAddressRow
        {
            SiFamily           = AfInet,
            Ipv4Addr           = addr,
            InterfaceLuid      = luid.Value,
            OnLinkPrefixLength = prefix,
            DadState           = DadStatePreferred,
            ValidLifetime      = InfiniteLifetime,
            PreferredLifetime  = InfiniteLifetime,
        };

        // Retry on error 87 (ERROR_INVALID_PARAMETER) and 1168 (ERROR_NOT_FOUND).
        // Both occur transiently while the adapter is completing registration in the
        // IP stack. 150 ms × 6 attempts = 900 ms worst case, well within user tolerance.
        uint err = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            err = CreateUnicastIpAddressEntry(in row);
            if (err == 0) break;
            if (err != 87 && err != 1168) break;
            System.Threading.Thread.Sleep(150);
        }

        if (err != 0)
        {
            System.Diagnostics.Debug.WriteLine($"[AdapterSetup] CreateUnicastIpAddressEntry failed: {err}");
            return null;
        }

        return new IpAddressContext(luid.Value, addr, prefix);
    }

    /// <summary>
    /// Fallback: assigns IPv4 by adapter friendly name when the LUID is unavailable
    /// (only possible with pre-0.14 wintun.dll which lacks WintunGetAdapterLuid).
    /// </summary>
    public static IpAddressContext? SetAdapterIp(IPAddress address, IPAddress subnetMask, string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return null;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            subnetMask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        ulong? luidValue = GetInterfaceLuidByName(adapterName);
        if (luidValue == null)
            return null;

        var luid = new Wintun.NetLuid { Value = luidValue.Value };
        return SetAdapterIp(address, subnetMask, luid);
    }

    /// <summary>
    /// Finds the NET_LUID for a network adapter whose Name or Description contains
    /// <paramref name="adapterName"/> (case-insensitive).
    /// </summary>
    private static ulong? GetInterfaceLuidByName(string adapterName)
    {
        var name = adapterName.Trim();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if ((ni.Name != null    && ni.Name.Contains(name,        StringComparison.OrdinalIgnoreCase)) ||
                (ni.Description != null && ni.Description.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                // Convert the adapter's GUID-based ID to a LUID via iphlpapi.
                // NetworkInterface.Id is the adapter GUID string ("{...}").
                if (Guid.TryParse(ni.Id, out var guid))
                {
                    if (ConvertInterfaceGuidToLuid(ref guid, out ulong luid) == 0)
                        return luid;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the interface index for an adapter whose Name or Description contains
    /// <paramref name="adapterName"/>. Fallback for GetInterfaceIndexByLuid when LUID
    /// is unavailable. Subject to NetworkInterface registration timing — prefer LUID path.
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

            if ((ni.Name        != null && ni.Name.Contains(name,        StringComparison.OrdinalIgnoreCase)) ||
                (ni.Description != null && ni.Description.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return (uint)ipv4.Index;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the interface index of the first active physical adapter that has a
    /// default IPv4 gateway. Used by SkipChina routing.
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

    private static byte MaskToPrefixLength(IPAddress mask)
    {
        uint m = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
        return (byte)BitOperations.PopCount(m);
    }

    /// <summary>
    /// Holds enough state to remove the address via DeleteUnicastIpAddressEntry.
    /// </summary>
    public readonly struct IpAddressContext
    {
        private readonly ulong _luid;
        private readonly uint  _addr;
        private readonly byte  _prefix;

        public IpAddressContext(ulong luid, uint addr, byte prefix)
        {
            _luid   = luid;
            _addr   = addr;
            _prefix = prefix;
        }

        public void Delete()
        {
            if (_luid == 0)
                return;

            var row = new MibUnicastIpAddressRow
            {
                SiFamily           = AfInet,
                Ipv4Addr           = _addr,
                InterfaceLuid      = _luid,
                OnLinkPrefixLength = _prefix,
                DadState           = DadStatePreferred,
                ValidLifetime      = InfiniteLifetime,
                PreferredLifetime  = InfiniteLifetime,
            };

            uint err = DeleteUnicastIpAddressEntry(in row);
            if (err != 0)
                System.Diagnostics.Debug.WriteLine($"[AdapterSetup] DeleteUnicastIpAddressEntry: {err}");
        }
    }
}
