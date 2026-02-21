using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using SSStap.PacketProcessing;
using SSStap.Routing;

namespace SSStap.Tunnel;

/// <summary>
/// Receives packets from Wintun (via IPacketSource), parses L3/L4, routes to SOCKS5/QUIC handlers,
/// and writes responses back. Coordinates routing, multiplexing, and packet-level filtering.
/// </summary>
public sealed class TunnelEngine
{
    private static readonly TimeSpan UdpFlowTtl = TimeSpan.FromMinutes(2);
    private const int UdpFlowPruneEvery = 256;

    private readonly IPacketSource _packetSource;
    private readonly ISocks5Client _socks5;
    private readonly RouteManager _routeManager;
    private readonly DnsInterceptCache _dnsInterceptCache = new();
    private readonly ConcurrentDictionary<ConnectionKey, TcpConnectionState> _tcpConnections = new();
    private readonly ConcurrentDictionary<UdpResponseKey, UdpFlowState> _udpFlowMap = new();
    private int _udpFlowTouchCount;
    private UdpRelayInfo? _udpRelay;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public TunnelEngine(
        IPacketSource packetSource,
        ISocks5Client socks5,
        RouteManager routeManager,
        RoutingMode routingMode)
    {
        _packetSource = packetSource;
        _socks5 = socks5;
        _routeManager = routeManager;
        _ = routingMode;
    }

    /// <summary>Starts the packet receive/forward loop.</summary>
    public void Start()
    {
        var oldCts = _cts;
        oldCts?.Cancel();
        oldCts?.Dispose();

        _cts = new CancellationTokenSource();
        _runTask = RunAsync(_cts.Token);
    }

    /// <summary>Stops the engine and tears down connections.</summary>
    public async Task StopAsync()
    {
        var cts = _cts;
        var runTask = _runTask;

        if (cts != null)
            await cts.CancelAsync();

        if (runTask != null)
            await runTask;

        cts?.Dispose();
        _cts = null;
        _runTask = null;

        _udpRelay?.Dispose();
        foreach (var kv in _tcpConnections)
            kv.Value.Dispose();
        _tcpConnections.Clear();
        _udpFlowMap.Clear();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var buffer = new byte[65536]; // Max IP packet size

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (parsed, packet) = await ReceiveForwardablePacketAsync(buffer, ct);
                if (parsed == null)
                    continue;

                await DispatchForwardablePacketAsync(parsed, packet, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Log and continue
                System.Diagnostics.Debug.WriteLine($"TunnelEngine: {ex.Message}");
            }
        }
    }

    private async Task<(ParsedPacket? Parsed, ReadOnlyMemory<byte> Packet)> ReceiveForwardablePacketAsync(byte[] buffer, CancellationToken ct)
    {
        var n = await _packetSource.ReceiveAsync(buffer, ct);
        if (n <= 0)
            return default;

        if (!IpPacketParser.TryParse(buffer.AsSpan(0, n), out var parsed) || parsed == null)
            return default;

        if (!parsed.IsForwardable)
            return default;

        if (!_routeManager.ShouldForwardViaProxy(parsed.DestinationAddress))
            return default;

        return (parsed, buffer.AsMemory(0, n));
    }

    private Task DispatchForwardablePacketAsync(ParsedPacket parsed, ReadOnlyMemory<byte> packet, CancellationToken ct)
    {
        return parsed.Transport switch
        {
            TransportProtocol.Tcp => HandleTcpAsync(parsed, packet, ct),
            TransportProtocol.Udp => HandleUdpAsync(parsed, packet, ct),
            _ => Task.CompletedTask,
        };
    }

    private async Task HandleTcpAsync(ParsedPacket parsed, ReadOnlyMemory<byte> packet, CancellationToken ct)
    {
        var key = new ConnectionKey(
            parsed.SourceAddress, parsed.SourcePort,
            parsed.DestinationAddress, parsed.DestinationPort);

        var state = _tcpConnections.GetOrAdd(key, k =>
        {
            var s = new TcpConnectionState();
            _ = EstablishTcpConnectionAsync(k, s, ct);
            return s;
        });

        state.ObserveClientPacket(parsed);

        bool clientSentReset = (parsed.TcpFlags & TcpControlBits.Rst) != 0;
        if (clientSentReset)
        {
            if (_tcpConnections.TryRemove(key, out var removed))
                removed.Dispose();
            return;
        }

        if (parsed.PayloadLength > 0 && state.Stream != null)
        {
            try
            {
                await state.Stream.WriteAsync(packet.Slice(parsed.PayloadOffset, parsed.PayloadLength), ct);
            }
            catch
            {
                _tcpConnections.TryRemove(key, out _);
                state.Dispose();
            }
        }
    }

    private async Task EstablishTcpConnectionAsync(ConnectionKey key, TcpConnectionState state, CancellationToken ct)
    {
        try
        {
            _dnsInterceptCache.TryGetHostname(key.DestAddress, out var hostname);
            var stream = await _socks5.ConnectTcpAsync(key.DestAddress, key.DestPort, hostname, ct);
            state.SetStream(stream);

            // Start read loop: proxy -> Wintun
            _ = RelayTcpFromProxyAsync(key, stream, state, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TCP connect failed {key}: {ex.Message}");
            _tcpConnections.TryRemove(key, out _);
            state.Dispose();
        }
    }

    private async Task RelayTcpFromProxyAsync(ConnectionKey key, Stream stream, TcpConnectionState state, CancellationToken ct)
    {
        var buffer = new byte[16384];
        bool sendFin = false;
        bool sendRst = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, ct);
                if (n <= 0)
                {
                    sendFin = true;
                    break;
                }

                if (!state.TryAllocateServerResponse(n, out var seqNum, out var ackNum))
                    continue;

                var responsePacket = PacketBuilder.BuildTcpResponse(
                    new PacketBuilder.TcpResponseContext(
                        key.DestAddress,
                        key.DestPort,
                        key.SrcAddress,
                        key.SrcPort,
                        seqNum,
                        ackNum,
                        TcpControlBits.Ack | TcpControlBits.Psh,
                        state.ClientWindowSize),
                    buffer.AsSpan(0, n));

                await _packetSource.SendAsync(responsePacket, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"TCP relay canceled {key}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TCP relay read error {key}: {ex.Message}");
            sendRst = true;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                if (sendRst)
                {
                    await TrySendTcpControlPacketAsync(key, state, TcpControlBits.Rst | TcpControlBits.Ack, ct);
                }
                else if (sendFin)
                {
                    await TrySendTcpControlPacketAsync(key, state, TcpControlBits.Fin | TcpControlBits.Ack, ct);
                }
            }

            _tcpConnections.TryRemove(key, out _);
            state.Dispose();
        }
    }

    private async Task TrySendTcpControlPacketAsync(ConnectionKey key, TcpConnectionState state, TcpControlBits flags, CancellationToken ct)
    {
        try
        {
            if (!state.TryAllocateServerControl(flags, out var seqNum, out var ackNum))
                return;

            var packet = PacketBuilder.BuildTcpResponse(
                new PacketBuilder.TcpResponseContext(
                    key.DestAddress,
                    key.DestPort,
                    key.SrcAddress,
                    key.SrcPort,
                    seqNum,
                    ackNum,
                    flags,
                    state.ClientWindowSize),
                Array.Empty<byte>());

            await _packetSource.SendAsync(packet, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TCP control packet send error {key}: {ex.Message}");
        }
    }

    private async Task HandleUdpAsync(ParsedPacket parsed, ReadOnlyMemory<byte> packet, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (!parsed.SourceAddress.Equals(IPAddress.Any) && !parsed.DestinationAddress.Equals(IPAddress.Any))
        {
            _udpFlowMap[new UdpResponseKey(parsed.DestinationAddress, parsed.DestinationPort)] =
                new UdpFlowState(parsed.SourceAddress, parsed.SourcePort, now);

            TouchUdpFlowMaintenance(now);
        }

        if (_udpRelay == null)
        {
            _udpRelay = await _socks5.OpenUdpAssociateAsync(ct);
            _udpRelay.UdpSocket = new UdpClient();
            _udpRelay.UdpSocket.Connect(_udpRelay.RelayEndPoint);
            _ = ReceiveUdpFromRelayAsync(_udpRelay, ct);
        }

        // SOCKS5 UDP format: RSV(2) FRAG(1) ATYP(1) DST.ADDR DST.PORT(2) DATA
        var payload = packet.Slice(parsed.PayloadOffset, parsed.PayloadLength);
        var destinationBytes = parsed.DestinationAddress.GetAddressBytes();
        byte atyp;
        byte[] header;

        if (destinationBytes.Length == 4)
        {
            atyp = 1;
            header = new byte[10];
            destinationBytes.CopyTo(header, 4);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), parsed.DestinationPort);
        }
        else if (destinationBytes.Length == 16)
        {
            atyp = 4;
            header = new byte[22];
            destinationBytes.CopyTo(header, 4);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(20), parsed.DestinationPort);
        }
        else
        {
            return;
        }

        header[2] = 0; // FRAG
        header[3] = atyp;

        var udpPacket = new byte[header.Length + payload.Length];
        header.CopyTo(udpPacket, 0);
        payload.CopyTo(udpPacket.AsMemory(header.Length));

        await _udpRelay.UdpSocket!.SendAsync(udpPacket, ct);
    }

    private async Task ReceiveUdpFromRelayAsync(UdpRelayInfo relay, CancellationToken ct)
    {
        if (relay.UdpSocket == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await relay.UdpSocket.ReceiveAsync(ct);
                var data = result.Buffer;
                if (!TryParseSocks5UdpEnvelope(data, out var envelope))
                    continue;

                if (envelope.Port == 53)
                    _dnsInterceptCache.InterceptDnsResponse(envelope.Payload.Span);

                var resolved = await ResolveUdpResponseFlowAsync(envelope, ct);
                if (resolved == null)
                    continue;

                var resolvedFlow = resolved.Value;

                if (resolvedFlow.RemoteAddress.AddressFamily != resolvedFlow.FlowState.LocalAddress.AddressFamily)
                    continue;

                var now = DateTime.UtcNow;
                _udpFlowMap[new UdpResponseKey(resolvedFlow.RemoteAddress, resolvedFlow.RemotePort)] =
                    resolvedFlow.FlowState with { LastSeenUtc = now };
                TouchUdpFlowMaintenance(now);

                var responsePayload = envelope.Payload.ToArray();
                var responsePacket = PacketBuilder.BuildUdpResponse(
                    resolvedFlow.RemoteAddress,
                    resolvedFlow.RemotePort,
                    resolvedFlow.FlowState.LocalAddress,
                    resolvedFlow.FlowState.LocalPort,
                    responsePayload);

                await _packetSource.SendAsync(responsePacket, ct);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("UDP relay receive canceled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UDP relay receive error: {ex.Message}");
        }
    }

    private async Task<ResolvedUdpFlow?> ResolveUdpResponseFlowAsync(Socks5UdpEnvelope envelope, CancellationToken ct)
    {
        var direct = TryResolveDirectFlow(envelope);
        if (direct != null)
            return direct;

        var domainResolved = await TryResolveDomainFlowAsync(envelope, ct);
        if (domainResolved != null)
            return domainResolved;

        return TryResolveFallbackFlow(envelope);
    }

    private ResolvedUdpFlow? TryResolveDirectFlow(Socks5UdpEnvelope envelope)
    {
        if (envelope.Address == null)
            return null;

        var directKey = new UdpResponseKey(envelope.Address, envelope.Port);
        if (!_udpFlowMap.TryGetValue(directKey, out var directFlow))
            return null;

        return new ResolvedUdpFlow(envelope.Address, envelope.Port, directFlow);
    }

    private async Task<ResolvedUdpFlow?> TryResolveDomainFlowAsync(Socks5UdpEnvelope envelope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envelope.Domain))
            return null;

        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(envelope.Domain, ct);
            foreach (var address in addresses)
            {
                var resolvedKey = new UdpResponseKey(address, envelope.Port);
                if (_udpFlowMap.TryGetValue(resolvedKey, out var resolvedFlow))
                    return new ResolvedUdpFlow(address, envelope.Port, resolvedFlow);
            }
        }
        catch
        {
            // Ignore DNS failures and continue to fallback matching.
        }

        return null;
    }

    private ResolvedUdpFlow? TryResolveFallbackFlow(Socks5UdpEnvelope envelope)
    {
        UdpResponseKey fallbackKey = default;
        UdpFlowState fallbackFlow = default;
        bool foundFallback = false;

        foreach (var kv in _udpFlowMap)
        {
            if (kv.Key.RemotePort != envelope.Port)
                continue;

            if (envelope.AddressFamily != AddressFamily.Unspecified && kv.Key.RemoteAddress.AddressFamily != envelope.AddressFamily)
                continue;

            if (!foundFallback || kv.Value.LastSeenUtc > fallbackFlow.LastSeenUtc)
            {
                foundFallback = true;
                fallbackKey = kv.Key;
                fallbackFlow = kv.Value;
            }
        }

        if (!foundFallback)
            return null;

        return new ResolvedUdpFlow(fallbackKey.RemoteAddress, fallbackKey.RemotePort, fallbackFlow);
    }

    private static bool TryParseSocks5UdpEnvelope(byte[] data, out Socks5UdpEnvelope envelope)
    {
        envelope = default;
        if (data.Length < 4)
            return false;

        if (data[2] != 0)
            return false;

        byte atyp = data[3];
        return atyp switch
        {
            1 => TryParseSocks5UdpIPv4Envelope(data, out envelope),
            3 => TryParseSocks5UdpDomainEnvelope(data, out envelope),
            4 => TryParseSocks5UdpIPv6Envelope(data, out envelope),
            _ => false,
        };
    }

    private static bool TryParseSocks5UdpIPv4Envelope(byte[] data, out Socks5UdpEnvelope envelope)
    {
        envelope = default;
        if (data.Length < 10)
            return false;

        var addressBytes = new byte[4];
        data.AsSpan(4, 4).CopyTo(addressBytes);
        var address = new IPAddress(addressBytes);
        ushort port = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8, 2));
        var payload = data.AsMemory(10);
        if (payload.Length == 0)
            return false;

        envelope = new Socks5UdpEnvelope(AddressFamily.InterNetwork, address, null, port, payload);
        return true;
    }

    private static bool TryParseSocks5UdpIPv6Envelope(byte[] data, out Socks5UdpEnvelope envelope)
    {
        envelope = default;
        if (data.Length < 22)
            return false;

        var addressBytes = new byte[16];
        data.AsSpan(4, 16).CopyTo(addressBytes);
        var address = new IPAddress(addressBytes);
        ushort port = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(20, 2));
        var payload = data.AsMemory(22);
        if (payload.Length == 0)
            return false;

        envelope = new Socks5UdpEnvelope(AddressFamily.InterNetworkV6, address, null, port, payload);
        return true;
    }

    private static bool TryParseSocks5UdpDomainEnvelope(byte[] data, out Socks5UdpEnvelope envelope)
    {
        envelope = default;
        if (data.Length < 5)
            return false;

        int domainLength = data[4];
        int headerLength = 7 + domainLength;
        if (data.Length < headerLength)
            return false;

        var domain = System.Text.Encoding.ASCII.GetString(data, 5, domainLength);
        ushort port = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(headerLength - 2, 2));
        var payload = data.AsMemory(headerLength);
        if (payload.Length == 0)
            return false;

        envelope = new Socks5UdpEnvelope(AddressFamily.Unspecified, null, domain, port, payload);
        return true;
    }

    private void TouchUdpFlowMaintenance(DateTime now)
    {
        if (Interlocked.Increment(ref _udpFlowTouchCount) % UdpFlowPruneEvery != 0)
            return;

        PruneUdpFlows(now);
    }

    private void PruneUdpFlows(DateTime now)
    {
        foreach (var kv in _udpFlowMap)
        {
            if (now - kv.Value.LastSeenUtc > UdpFlowTtl)
                _udpFlowMap.TryRemove(kv.Key, out _);
        }
    }

    private readonly record struct ConnectionKey(
        IPAddress SrcAddress, ushort SrcPort,
        IPAddress DestAddress, ushort DestPort);

    private readonly record struct UdpResponseKey(
        IPAddress RemoteAddress,
        ushort RemotePort);

    private readonly record struct Socks5UdpEnvelope(
        AddressFamily AddressFamily,
        IPAddress? Address,
        string? Domain,
        ushort Port,
        ReadOnlyMemory<byte> Payload);

    private readonly record struct ResolvedUdpFlow(
        IPAddress RemoteAddress,
        ushort RemotePort,
        UdpFlowState FlowState);

    private readonly record struct UdpFlowState(
        IPAddress LocalAddress,
        ushort LocalPort,
        DateTime LastSeenUtc);

    private sealed class TcpConnectionState : IDisposable
    {
        private Stream? _stream;
        private readonly object _lock = new();
        private bool _disposed;
        private bool _serverClosed;
        private uint _clientNextSequence;
        private uint _serverNextSequence;
        private bool _hasClientSequence;
        private bool _hasServerSequence;
        private ushort _clientWindowSize = 65535;

        public Stream? Stream => _stream;
        public ushort ClientWindowSize
        {
            get
            {
                lock (_lock)
                {
                    return _clientWindowSize == 0 ? (ushort)65535 : _clientWindowSize;
                }
            }
        }

        public void ObserveClientPacket(ParsedPacket parsed)
        {
            if (parsed.Transport != TransportProtocol.Tcp)
                return;

            lock (_lock)
            {
                _clientWindowSize = parsed.TcpWindowSize == 0 ? _clientWindowSize : parsed.TcpWindowSize;

                uint wireLength = (uint)parsed.PayloadLength;
                if ((parsed.TcpFlags & TcpControlBits.Syn) != 0)
                    wireLength += 1;
                if ((parsed.TcpFlags & TcpControlBits.Fin) != 0)
                    wireLength += 1;

                uint nextClientSeq = parsed.TcpSequenceNumber + wireLength;
                if (!_hasClientSequence || SequenceGreaterThan(nextClientSeq, _clientNextSequence))
                {
                    _clientNextSequence = nextClientSeq;
                    _hasClientSequence = true;
                }

                if (parsed.TcpAcknowledgmentNumber > 0 &&
                    (!_hasServerSequence || SequenceGreaterThan(parsed.TcpAcknowledgmentNumber, _serverNextSequence)))
                {
                    _serverNextSequence = parsed.TcpAcknowledgmentNumber;
                    _hasServerSequence = true;
                }

            }
        }

        public bool TryAllocateServerResponse(int payloadLength, out uint sequenceNumber, out uint acknowledgementNumber)
        {
            lock (_lock)
            {
                sequenceNumber = 0;
                acknowledgementNumber = 0;

                if (_serverClosed || !_hasClientSequence)
                    return false;

                if (!_hasServerSequence)
                {
                    _serverNextSequence = 1;
                    _hasServerSequence = true;
                }

                sequenceNumber = _serverNextSequence;
                acknowledgementNumber = _clientNextSequence;
                _serverNextSequence += (uint)Math.Max(payloadLength, 0);
                return true;
            }
        }

        public bool TryAllocateServerControl(TcpControlBits flags, out uint sequenceNumber, out uint acknowledgementNumber)
        {
            lock (_lock)
            {
                sequenceNumber = 0;
                acknowledgementNumber = 0;

                if (_serverClosed || !_hasClientSequence)
                    return false;

                if (!_hasServerSequence)
                {
                    _serverNextSequence = 1;
                    _hasServerSequence = true;
                }

                sequenceNumber = _serverNextSequence;
                acknowledgementNumber = _clientNextSequence;

                uint controlSequenceAdvance = 0;
                if ((flags & TcpControlBits.Fin) != 0)
                    controlSequenceAdvance += 1;
                if ((flags & TcpControlBits.Syn) != 0)
                    controlSequenceAdvance += 1;

                _serverNextSequence += controlSequenceAdvance;

                if ((flags & (TcpControlBits.Fin | TcpControlBits.Rst)) != 0)
                    _serverClosed = true;

                return true;
            }
        }

        private static bool SequenceGreaterThan(uint a, uint b)
            => unchecked((int)(a - b)) > 0;

        public void SetStream(Stream s)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    s.Dispose();
                    return;
                }
                _stream = s;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _stream?.Dispose();
            }
        }
    }
}
