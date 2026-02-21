using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SSStap.PacketProcessing;

namespace SSStap.Tunnel;

public static class PacketBuilder
{
    private const int Ipv4HeaderLength = 20;
    private const int Ipv6HeaderLength = 40;
    private const int TcpHeaderLength = 20;
    private const int UdpHeaderLength = 8;

    public readonly record struct TcpResponseContext(
        IPAddress SrcIp,
        ushort SrcPort,
        IPAddress DstIp,
        ushort DstPort,
        uint SequenceNumber,
        uint AcknowledgementNumber,
        TcpControlBits Flags,
        ushort WindowSize);

    public static byte[] BuildTcpResponse(
        TcpResponseContext context,
        ReadOnlySpan<byte> payload)
    {
        var srcBytes = GetIPv4Bytes(context.SrcIp);
        var dstBytes = GetIPv4Bytes(context.DstIp);

        int tcpLength = TcpHeaderLength + payload.Length;
        int totalLength = Ipv4HeaderLength + tcpLength;
        var packet = new byte[totalLength];

        WriteIpv4Header(packet.AsSpan(0, Ipv4HeaderLength), srcBytes, dstBytes, (byte)TransportProtocol.Tcp, totalLength);

        var tcpHeader = packet.AsSpan(Ipv4HeaderLength, TcpHeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(tcpHeader.Slice(0, 2), context.SrcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcpHeader.Slice(2, 2), context.DstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcpHeader.Slice(4, 4), context.SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(tcpHeader.Slice(8, 4), context.AcknowledgementNumber);
        tcpHeader[12] = 0x50;
        tcpHeader[13] = (byte)context.Flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcpHeader.Slice(14, 2), context.WindowSize);
        tcpHeader[16] = 0;
        tcpHeader[17] = 0;
        tcpHeader[18] = 0;
        tcpHeader[19] = 0;

        payload.CopyTo(packet.AsSpan(Ipv4HeaderLength + TcpHeaderLength));

        ushort tcpChecksum = ComputeTransportChecksumV4(srcBytes, dstBytes, (byte)TransportProtocol.Tcp, packet.AsSpan(Ipv4HeaderLength, tcpLength));
        BinaryPrimitives.WriteUInt16BigEndian(tcpHeader.Slice(16, 2), tcpChecksum);

        return packet;
    }

    public static byte[] BuildUdpResponse(
        IPAddress srcIp,
        ushort srcPort,
        IPAddress dstIp,
        ushort dstPort,
        ReadOnlySpan<byte> payload)
    {
        if (srcIp.AddressFamily != dstIp.AddressFamily)
            throw new ArgumentException("Source and destination address families must match.");

        if (srcIp.AddressFamily == AddressFamily.InterNetwork)
            return BuildUdpResponseIPv4(srcIp, srcPort, dstIp, dstPort, payload);

        if (srcIp.AddressFamily == AddressFamily.InterNetworkV6)
            return BuildUdpResponseIPv6(srcIp, srcPort, dstIp, dstPort, payload);

        throw new NotSupportedException($"Address family not supported: {srcIp.AddressFamily}");
    }

    private static byte[] BuildUdpResponseIPv4(
        IPAddress srcIp,
        ushort srcPort,
        IPAddress dstIp,
        ushort dstPort,
        ReadOnlySpan<byte> payload)
    {
        var srcBytes = GetIPv4Bytes(srcIp);
        var dstBytes = GetIPv4Bytes(dstIp);

        int udpLength = UdpHeaderLength + payload.Length;
        int totalLength = Ipv4HeaderLength + udpLength;
        var packet = new byte[totalLength];

        WriteIpv4Header(packet.AsSpan(0, Ipv4HeaderLength), srcBytes, dstBytes, (byte)TransportProtocol.Udp, totalLength);

        var udpHeader = packet.AsSpan(Ipv4HeaderLength, UdpHeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(0, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(2, 2), dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(4, 2), (ushort)udpLength);
        udpHeader[6] = 0;
        udpHeader[7] = 0;

        payload.CopyTo(packet.AsSpan(Ipv4HeaderLength + UdpHeaderLength));

        ushort udpChecksum = ComputeTransportChecksumV4(srcBytes, dstBytes, (byte)TransportProtocol.Udp, packet.AsSpan(Ipv4HeaderLength, udpLength));
        if (udpChecksum == 0)
            udpChecksum = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(6, 2), udpChecksum);

        return packet;
    }

    private static byte[] BuildUdpResponseIPv6(
        IPAddress srcIp,
        ushort srcPort,
        IPAddress dstIp,
        ushort dstPort,
        ReadOnlySpan<byte> payload)
    {
        var srcBytes = GetIPv6Bytes(srcIp);
        var dstBytes = GetIPv6Bytes(dstIp);

        int udpLength = UdpHeaderLength + payload.Length;
        int totalLength = Ipv6HeaderLength + udpLength;
        var packet = new byte[totalLength];

        WriteIpv6Header(packet.AsSpan(0, Ipv6HeaderLength), srcBytes, dstBytes, (byte)TransportProtocol.Udp, udpLength);

        var udpHeader = packet.AsSpan(Ipv6HeaderLength, UdpHeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(0, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(2, 2), dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(4, 2), (ushort)udpLength);
        udpHeader[6] = 0;
        udpHeader[7] = 0;

        payload.CopyTo(packet.AsSpan(Ipv6HeaderLength + UdpHeaderLength));

        ushort udpChecksum = ComputeTransportChecksumV6(srcBytes, dstBytes, (byte)TransportProtocol.Udp, packet.AsSpan(Ipv6HeaderLength, udpLength));
        if (udpChecksum == 0)
            udpChecksum = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(udpHeader.Slice(6, 2), udpChecksum);

        return packet;
    }

    private static void WriteIpv4Header(Span<byte> header, ReadOnlySpan<byte> srcBytes, ReadOnlySpan<byte> dstBytes, byte protocol, int totalLength)
    {
        header.Clear();
        header[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), (ushort)totalLength);
        header[8] = 64;
        header[9] = protocol;
        srcBytes.CopyTo(header.Slice(12, 4));
        dstBytes.CopyTo(header.Slice(16, 4));

        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(10, 2), 0);
        ushort checksum = ComputeInternetChecksum(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(10, 2), checksum);
    }

    private static void WriteIpv6Header(Span<byte> header, ReadOnlySpan<byte> srcBytes, ReadOnlySpan<byte> dstBytes, byte nextHeader, int payloadLength)
    {
        header.Clear();
        header[0] = 0x60;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4, 2), (ushort)payloadLength);
        header[6] = nextHeader;
        header[7] = 64;
        srcBytes.CopyTo(header.Slice(8, 16));
        dstBytes.CopyTo(header.Slice(24, 16));
    }

    private static byte[] GetIPv4Bytes(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        if (bytes.Length != 4)
            throw new ArgumentException("IPv4 address expected", nameof(ipAddress));
        return bytes;
    }

    private static byte[] GetIPv6Bytes(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        if (bytes.Length != 16)
            throw new ArgumentException("IPv6 address expected", nameof(ipAddress));
        return bytes;
    }

    private static ushort ComputeTransportChecksumV4(ReadOnlySpan<byte> src, ReadOnlySpan<byte> dst, byte protocol, ReadOnlySpan<byte> segment)
    {
        int pseudoLength = 12 + segment.Length;
        var pseudoPacket = new byte[pseudoLength];

        src.CopyTo(pseudoPacket.AsSpan(0, 4));
        dst.CopyTo(pseudoPacket.AsSpan(4, 4));
        pseudoPacket[8] = 0;
        pseudoPacket[9] = protocol;
        BinaryPrimitives.WriteUInt16BigEndian(pseudoPacket.AsSpan(10, 2), (ushort)segment.Length);
        segment.CopyTo(pseudoPacket.AsSpan(12));

        return ComputeInternetChecksum(pseudoPacket);
    }

    private static ushort ComputeTransportChecksumV6(ReadOnlySpan<byte> src, ReadOnlySpan<byte> dst, byte protocol, ReadOnlySpan<byte> segment)
    {
        int pseudoLength = 40 + segment.Length;
        var pseudoPacket = new byte[pseudoLength];

        src.CopyTo(pseudoPacket.AsSpan(0, 16));
        dst.CopyTo(pseudoPacket.AsSpan(16, 16));
        BinaryPrimitives.WriteUInt32BigEndian(pseudoPacket.AsSpan(32, 4), (uint)segment.Length);
        pseudoPacket[39] = protocol;
        segment.CopyTo(pseudoPacket.AsSpan(40));

        return ComputeInternetChecksum(pseudoPacket);
    }

    private static ushort ComputeInternetChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        while (i + 1 < data.Length)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i, 2));
            i += 2;
        }

        if (i < data.Length)
            sum += (uint)(data[i] << 8);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }
}
