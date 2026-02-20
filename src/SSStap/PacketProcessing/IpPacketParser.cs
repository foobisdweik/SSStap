using System.Net;

namespace SSStap.PacketProcessing;

/// <summary>
/// Minimal L3/L4 parser for raw IP packets from Wintun.
/// Parses IPv4 header + TCP/UDP headers without external dependencies.
/// See RFC 791 (IP), RFC 793 (TCP), RFC 768 (UDP).
///
/// Alternative: lwIP could be used via P/Invoke for full stack semantics,
/// but adds native dependency; a minimal managed parser is sufficient for
/// routing decisions (src/dst IP/port, protocol) and payload extraction.
/// </summary>
public static class IpPacketParser
{
    private const int MinIpHeaderSize = 20;
    private const int MinTcpHeaderSize = 20;
    private const int UdpHeaderSize = 8;

    /// <summary>
    /// Attempts to parse a raw IPv4 packet into L3/L4 structure.
    /// </summary>
    /// <param name="buffer">Raw packet buffer from Wintun.</param>
    /// <param name="parsed">Parsed result, or null if parse fails.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out ParsedPacket? parsed)
    {
        parsed = null;
        if (buffer.Length < MinIpHeaderSize)
            return false;

        byte versionIhl = buffer[0];
        byte version = (byte)(versionIhl >> 4);
        int ihl = (versionIhl & 0x0F) * 4;
        if (version != 4 || ihl < MinIpHeaderSize)
            return false;

        ushort totalLength = (ushort)((buffer[2] << 8) | buffer[3]);
        if (buffer.Length < totalLength || totalLength < ihl)
            return false;

        byte protocol = buffer[9];
        var srcIp = new byte[4];
        var dstIp = new byte[4];
        buffer.Slice(12, 4).CopyTo(srcIp);
        buffer.Slice(16, 4).CopyTo(dstIp);

        int srcPort, dstPort;
        int payloadOffset;
        int payloadLength;

        if (protocol == 6) // TCP
        {
            if (totalLength < ihl + MinTcpHeaderSize)
                return false;
            var tcpStart = buffer.Slice(ihl);
            srcPort = (tcpStart[0] << 8) | tcpStart[1];
            dstPort = (tcpStart[2] << 8) | tcpStart[3];
            int tcpDataOffset = ((tcpStart[12] >> 4) & 0x0F) * 4;
            if (tcpDataOffset < MinTcpHeaderSize)
                return false;
            payloadOffset = ihl + tcpDataOffset;
            payloadLength = totalLength - payloadOffset;
        }
        else if (protocol == 17) // UDP
        {
            if (totalLength < ihl + UdpHeaderSize)
                return false;
            var udpStart = buffer.Slice(ihl);
            srcPort = (udpStart[0] << 8) | udpStart[1];
            dstPort = (udpStart[2] << 8) | udpStart[3];
            payloadOffset = ihl + UdpHeaderSize;
            payloadLength = totalLength - payloadOffset;
        }
        else
        {
            srcPort = 0;
            dstPort = 0;
            payloadOffset = ihl;
            payloadLength = totalLength - ihl;
        }

        parsed = new ParsedPacket
        {
            Version = version,
            Protocol = protocol,
            TotalLength = totalLength,
            IpHeaderLength = ihl,
            SourceAddress = new IPAddress(srcIp),
            DestinationAddress = new IPAddress(dstIp),
            SourcePort = (ushort)srcPort,
            DestinationPort = (ushort)dstPort,
            PayloadOffset = payloadOffset,
            PayloadLength = payloadLength,
        };
        return true;
    }
}
