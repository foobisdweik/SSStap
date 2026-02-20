namespace SSStap.Tunnel;

/// <summary>
/// Abstraction for receiving raw IP packets (e.g. from Wintun ring buffer).
/// Implemented by Wintun integration in Phase 2.
/// </summary>
public interface IPacketSource
{
    /// <summary>Receives a packet. Returns number of bytes read, or 0 if none available / closed.</summary>
    /// <param name="buffer">Buffer to write packet data into.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default);

    /// <summary>Writes a packet back (response from proxy).</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default);
}
