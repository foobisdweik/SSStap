using System.Collections.Concurrent;
using System.IO;
using System.Net;
using SSStap.PacketProcessing;
using SSStap.Routing;

namespace SSStap.Tunnel;

/// <summary>
/// Receives packets from Wintun (via IPacketSource), parses L3/L4, routes to SOCKS5/QUIC handlers,
/// and writes responses back. Coordinates routing, multiplexing, and packet-level filtering.
/// </summary>
public sealed class TunnelEngine
{
    private readonly IPacketSource _packetSource;
    private readonly ISocks5Client _socks5;
    private readonly RouteManager _routeManager;
    private readonly RoutingMode _routingMode;
    private readonly ConcurrentDictionary<ConnectionKey, TcpConnectionState> _tcpConnections = new();
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
        _routingMode = routingMode;
    }

    /// <summary>Starts the packet receive/forward loop.</summary>
    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _runTask = RunAsync(_cts.Token);
    }

    /// <summary>Stops the engine and tears down connections.</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_runTask != null)
            await _runTask;
        _udpRelay?.Dispose();
        foreach (var kv in _tcpConnections)
            kv.Value.Dispose();
        _tcpConnections.Clear();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var buffer = new byte[65536]; // Max IP packet size

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = await _packetSource.ReceiveAsync(buffer, ct);
                if (n <= 0) continue;

                if (!IpPacketParser.TryParse(buffer.AsSpan(0, n), out var parsed) || parsed == null)
                    continue;

                if (!parsed.IsForwardable)
                    continue;

                if (!_routeManager.ShouldForwardViaProxy(parsed.DestinationAddress))
                    continue; // Packets to non-proxy destinations (e.g. China in SkipChina) - would need reinjection

                if (parsed.Transport == TransportProtocol.Tcp)
                {
                    await HandleTcpAsync(parsed, buffer.AsMemory(0, n), ct);
                }
                else if (parsed.Transport == TransportProtocol.Udp)
                {
                    await HandleUdpAsync(parsed, buffer.AsMemory(0, n), ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Log and continue
                System.Diagnostics.Debug.WriteLine($"TunnelEngine: {ex.Message}");
            }
        }
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
            var stream = await _socks5.ConnectTcpAsync(key.DestAddress, key.DestPort, ct);
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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, ct);
                if (n <= 0) break;

                // Build IP+TCP response packet and send to Wintun
                // Simplified: we need to construct response packet (swap src/dst, etc.)
                // Full implementation requires IP/TCP header construction
                await _packetSource.SendAsync(buffer.AsMemory(0, n), ct);
            }
        }
        finally
        {
            _tcpConnections.TryRemove(key, out _);
            state.Dispose();
        }
    }

    private async Task HandleUdpAsync(ParsedPacket parsed, ReadOnlyMemory<byte> packet, CancellationToken ct)
    {
        if (_udpRelay == null)
        {
            _udpRelay = await _socks5.OpenUdpAssociateAsync(ct);
        }

        // SOCKS5 UDP format: RSV(2) FRAG(1) ATYP(1) DST.ADDR(4) DST.PORT(2) DATA
        var header = new byte[10];
        header[2] = 0; // FRAG
        header[3] = 1; // ATYP IPv4
        parsed.DestinationAddress.GetAddressBytes().CopyTo(header, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), parsed.DestinationPort);

        // TODO: use actual UDP socket to send to _udpRelay.RelayEndPoint
        // For now, stub - requires UdpClient and receive loop for responses
    }

    private readonly record struct ConnectionKey(
        IPAddress SrcAddress, ushort SrcPort,
        IPAddress DestAddress, ushort DestPort);

    private sealed class TcpConnectionState : IDisposable
    {
        private Stream? _stream;
        private readonly object _lock = new();

        public Stream? Stream => _stream;

        public void SetStream(Stream s)
        {
            lock (_lock) { _stream = s; }
        }

        public void Dispose() => _stream?.Dispose();
    }
}
