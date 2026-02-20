using System.Net;

namespace SSStap.PacketProcessing;

/// <summary>
/// Transport protocol parsed from L4 header.
/// </summary>
public enum TransportProtocol
{
    Unknown,
    Tcp = 6,
    Udp = 17,
    Icmp = 1,
}

/// <summary>
/// Result of parsing a raw IP packet from Wintun.
/// Contains L3 (IP) and L4 (TCP/UDP) header info and payload.
/// </summary>
public sealed class ParsedPacket
{
    /// <summary>IP version (4 for IPv4).</summary>
    public byte Version { get; init; }

    /// <summary>IP protocol: TCP=6, UDP=17.</summary>
    public byte Protocol { get; init; }

    /// <summary>Total IP packet length in bytes.</summary>
    public ushort TotalLength { get; init; }

    /// <summary>IP header length in bytes (typically 20).</summary>
    public int IpHeaderLength { get; init; }

    /// <summary>Source IP address.</summary>
    public IPAddress SourceAddress { get; init; } = IPAddress.None;

    /// <summary>Destination IP address.</summary>
    public IPAddress DestinationAddress { get; init; } = IPAddress.None;

    /// <summary>Source port (TCP/UDP).</summary>
    public ushort SourcePort { get; init; }

    /// <summary>Destination port (TCP/UDP).</summary>
    public ushort DestinationPort { get; init; }

    /// <summary>Offset of L4 payload within the raw packet.</summary>
    public int PayloadOffset { get; init; }

    /// <summary>Length of L4 payload in bytes.</summary>
    public int PayloadLength { get; init; }

    /// <summary>Transported protocol.</summary>
    public TransportProtocol Transport => Protocol switch
    {
        6 => TransportProtocol.Tcp,
        17 => TransportProtocol.Udp,
        1 => TransportProtocol.Icmp,
        _ => TransportProtocol.Unknown,
    };

    /// <summary>Whether this packet is valid for forwarding (TCP or UDP).</summary>
    public bool IsForwardable => Transport is TransportProtocol.Tcp or TransportProtocol.Udp;
}
