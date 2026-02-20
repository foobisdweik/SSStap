using System.Net;

namespace SSStap.Tunnel;

/// <summary>
/// Stub for QUIC/HTTP3 forwarding via libcurl.
/// Future: use libcurl built with ngtcp2+nghttp3 for HTTP/3 requests over QUIC.
/// </summary>
public static class QuicHandler
{
    /// <summary>Placeholder for future QUIC tunnel. Not implemented.</summary>
    public static Task<bool> IsQuicPacketAsync(ReadOnlySpan<byte> packet)
    {
        // QUIC packets typically start with connection IDs; detect via first byte
        // Format: flag byte, version (4 bytes for negotiable), etc.
        return Task.FromResult(false);
    }

    /// <summary>Placeholder for forwarding a QUIC packet via HTTP/3. Not implemented.</summary>
    public static Task ForwardQuicAsync(IPAddress target, int port, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        throw new NotImplementedException("QUIC/HTTP3 support requires libcurl with ngtcp2+nghttp3. Stub for future implementation.");
    }
}
