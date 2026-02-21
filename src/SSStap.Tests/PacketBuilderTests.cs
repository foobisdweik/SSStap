using System.Net;
using System.Buffers.Binary;
using SSStap.PacketProcessing;
using SSStap.Tunnel;
using Xunit;

namespace SSStap.Tests;

public class PacketBuilderTests
{
    [Fact]
    public void BuildTcpResponse_ParserReadsTcpMetadata()
    {
        var srcIp = IPAddress.Parse("8.8.8.8");
        var dstIp = IPAddress.Parse("10.0.0.2");
        ushort srcPort = 443;
        ushort dstPort = 51234;
        uint seq = 123456789;
        uint ack = 987654321;
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var packet = PacketBuilder.BuildTcpResponse(
            new PacketBuilder.TcpResponseContext(
                srcIp,
                srcPort,
                dstIp,
                dstPort,
                seq,
                ack,
                TcpControlBits.Ack | TcpControlBits.Psh,
                4096),
            payload);

        var parsedOk = IpPacketParser.TryParse(packet, out var parsed);

        Assert.True(parsedOk);
        Assert.NotNull(parsed);
        Assert.Equal(TransportProtocol.Tcp, parsed.Transport);
        Assert.Equal(srcIp, parsed.SourceAddress);
        Assert.Equal(dstIp, parsed.DestinationAddress);
        Assert.Equal(srcPort, parsed.SourcePort);
        Assert.Equal(dstPort, parsed.DestinationPort);
        Assert.Equal(seq, parsed.TcpSequenceNumber);
        Assert.Equal(ack, parsed.TcpAcknowledgmentNumber);
        Assert.Equal(TcpControlBits.Ack | TcpControlBits.Psh, parsed.TcpFlags & (TcpControlBits.Ack | TcpControlBits.Psh));
        Assert.Equal(4096, parsed.TcpWindowSize);
        Assert.Equal(payload.Length, parsed.PayloadLength);
    }

    [Fact]
    public void BuildUdpResponse_ParserReadsPortsAndPayload()
    {
        var srcIp = IPAddress.Parse("1.1.1.1");
        var dstIp = IPAddress.Parse("10.0.0.3");
        ushort srcPort = 53;
        ushort dstPort = 53000;
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var packet = PacketBuilder.BuildUdpResponse(
            srcIp,
            srcPort,
            dstIp,
            dstPort,
            payload);

        var parsedOk = IpPacketParser.TryParse(packet, out var parsed);

        Assert.True(parsedOk);
        Assert.NotNull(parsed);
        Assert.Equal(TransportProtocol.Udp, parsed.Transport);
        Assert.Equal(srcIp, parsed.SourceAddress);
        Assert.Equal(dstIp, parsed.DestinationAddress);
        Assert.Equal(srcPort, parsed.SourcePort);
        Assert.Equal(dstPort, parsed.DestinationPort);
        Assert.Equal(payload.Length, parsed.PayloadLength);

        var packetPayload = packet.AsSpan(parsed.PayloadOffset, parsed.PayloadLength).ToArray();
        Assert.Equal(payload, packetPayload);
    }

    [Theory]
    [InlineData(TcpControlBits.Fin | TcpControlBits.Ack)]
    [InlineData(TcpControlBits.Rst | TcpControlBits.Ack)]
    public void BuildTcpResponse_ControlFlagsArePreserved(TcpControlBits flags)
    {
        var srcIp = IPAddress.Parse("9.9.9.9");
        var dstIp = IPAddress.Parse("10.0.0.4");

        var packet = PacketBuilder.BuildTcpResponse(
            new PacketBuilder.TcpResponseContext(
                srcIp,
                443,
                dstIp,
                52345,
                100,
                200,
                flags,
                2048),
            Array.Empty<byte>());

        var parsedOk = IpPacketParser.TryParse(packet, out var parsed);

        Assert.True(parsedOk);
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.PayloadLength);
        Assert.Equal(flags, parsed.TcpFlags & flags);
    }

    [Fact]
    public void BuildUdpResponse_Ipv6_WritesValidIpv6UdpPacket()
    {
        var srcIp = IPAddress.Parse("2001:4860:4860::8888");
        var dstIp = IPAddress.Parse("fd00::1234");
        ushort srcPort = 53;
        ushort dstPort = 53000;
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        var packet = PacketBuilder.BuildUdpResponse(srcIp, srcPort, dstIp, dstPort, payload);

        Assert.Equal(40 + 8 + payload.Length, packet.Length);
        Assert.Equal(6, packet[0] >> 4);
        Assert.Equal(8 + payload.Length, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2)));
        Assert.Equal((byte)TransportProtocol.Udp, packet[6]);
        Assert.Equal(64, packet[7]);

        Assert.Equal(srcIp.GetAddressBytes(), packet.AsSpan(8, 16).ToArray());
        Assert.Equal(dstIp.GetAddressBytes(), packet.AsSpan(24, 16).ToArray());

        Assert.Equal(srcPort, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(40, 2)));
        Assert.Equal(dstPort, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(42, 2)));
        Assert.Equal(8 + payload.Length, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(44, 2)));
        Assert.NotEqual((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(46, 2)));
        Assert.Equal(payload, packet.AsSpan(48, payload.Length).ToArray());
    }

    [Fact]
    public void BuildUdpResponse_MixedAddressFamilies_Throws()
    {
        var srcIp = IPAddress.Parse("1.1.1.1");
        var dstIp = IPAddress.Parse("fd00::1234");

        Assert.Throws<ArgumentException>(() =>
            PacketBuilder.BuildUdpResponse(srcIp, 53, dstIp, 53000, new byte[] { 0x01 }));
    }
}
