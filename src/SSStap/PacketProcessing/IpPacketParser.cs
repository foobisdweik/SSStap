using System.Net;
using System.Buffers.Binary;

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

        if (!TryParseTransport(buffer, protocol, ihl, totalLength, out var transport))
        {
            return false;
        }

        parsed = new ParsedPacket
        {
            Version = version,
            Protocol = protocol,
            TotalLength = totalLength,
            IpHeaderLength = ihl,
            SourceAddress = new IPAddress(srcIp),
            DestinationAddress = new IPAddress(dstIp),
            SourcePort = (ushort)transport.SourcePort,
            DestinationPort = (ushort)transport.DestinationPort,
            PayloadOffset = transport.PayloadOffset,
            PayloadLength = transport.PayloadLength,
            TcpSequenceNumber = transport.TcpSequenceNumber,
            TcpAcknowledgmentNumber = transport.TcpAcknowledgmentNumber,
            TcpHeaderLength = transport.TcpHeaderLength,
            TcpFlags = transport.TcpFlags,
            TcpWindowSize = transport.TcpWindowSize,
        };
        return true;
    }

    private static bool TryParseTransport(
        ReadOnlySpan<byte> buffer,
        byte protocol,
        int ihl,
        ushort totalLength,
        out TransportParseResult result)
    {
        result = new TransportParseResult(
            SourcePort: 0,
            DestinationPort: 0,
            PayloadOffset: ihl,
            PayloadLength: totalLength - ihl,
            TcpSequenceNumber: 0,
            TcpAcknowledgmentNumber: 0,
            TcpHeaderLength: 0,
            TcpFlags: 0,
            TcpWindowSize: 0);

        if (protocol == 6)
        {
            if (totalLength < ihl + MinTcpHeaderSize)
                return false;

            var tcpStart = buffer.Slice(ihl);
            int srcPort = (tcpStart[0] << 8) | tcpStart[1];
            int dstPort = (tcpStart[2] << 8) | tcpStart[3];
            int tcpDataOffset = ((tcpStart[12] >> 4) & 0x0F) * 4;

            if (tcpDataOffset < MinTcpHeaderSize || totalLength < ihl + tcpDataOffset)
                return false;

            uint tcpSeq = BinaryPrimitives.ReadUInt32BigEndian(tcpStart.Slice(4, 4));
            uint tcpAck = BinaryPrimitives.ReadUInt32BigEndian(tcpStart.Slice(8, 4));
            var tcpFlags = (TcpControlBits)tcpStart[13];
            ushort tcpWindow = BinaryPrimitives.ReadUInt16BigEndian(tcpStart.Slice(14, 2));
            int payloadOffset = ihl + tcpDataOffset;
            int payloadLength = totalLength - payloadOffset;

            result = new TransportParseResult(
                srcPort,
                dstPort,
                payloadOffset,
                payloadLength,
                tcpSeq,
                tcpAck,
                tcpDataOffset,
                tcpFlags,
                tcpWindow);
            return true;
        }

        if (protocol == 17)
        {
            if (totalLength < ihl + UdpHeaderSize)
                return false;

            var udpStart = buffer.Slice(ihl);
            int srcPort = (udpStart[0] << 8) | udpStart[1];
            int dstPort = (udpStart[2] << 8) | udpStart[3];
            int payloadOffset = ihl + UdpHeaderSize;
            int payloadLength = totalLength - payloadOffset;

            result = new TransportParseResult(
                srcPort,
                dstPort,
                payloadOffset,
                payloadLength,
                0,
                0,
                0,
                0,
                0);
        }

        return true;
    }

    private readonly record struct TransportParseResult(
        int SourcePort,
        int DestinationPort,
        int PayloadOffset,
        int PayloadLength,
        uint TcpSequenceNumber,
        uint TcpAcknowledgmentNumber,
        int TcpHeaderLength,
        TcpControlBits TcpFlags,
        ushort TcpWindowSize);
}
