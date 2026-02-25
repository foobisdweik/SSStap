using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SSStap.Tunnel;

/// <summary>
/// Applies DSCP/TOS markings to sockets to ensure priority over Wi-Fi/Hotspot links (Finding 5).
/// </summary>
public static class QosHelper
{
    // DSCP Values
    // CS5 (Class Selector 5) = 0x28. Left-shifted 2 bits for TOS byte = 0xA0.
    private const int DscpCS5 = 0x28 << 2; 
    // EF (Expedited Forwarding) = 0x2E. Left-shifted 2 bits for TOS byte = 0xB8.
    private const int DscpEF = 0x2E << 2;

    private const int IPPROTO_IP = 0;
    private const int IPPROTO_IPV6 = 41;
    private const int IP_TOS = 3;
    private const int IPV6_TCLASS = 39;

    /// <summary>Applies CS5 (Signaling/High Priority) to TCP sockets.</summary>
    public static void ApplyTcpQos(Socket socket) => ApplyDscp(socket, DscpCS5);

    /// <summary>Applies EF (Expedited Forwarding) to UDP sockets for lowest latency.</summary>
    public static void ApplyUdpQos(Socket socket) => ApplyDscp(socket, DscpEF);

    private static void ApplyDscp(Socket socket, int tos)
    {
        try
        {
            if (socket.AddressFamily == AddressFamily.InterNetwork)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, tos);
            }
            else if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // .NET doesn't always have a named enum for TCLASS on all platforms
                socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)IPV6_TCLASS, tos);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QoS: Failed to set DSCP {tos:X2}: {ex.Message}");
        }
    }
}
