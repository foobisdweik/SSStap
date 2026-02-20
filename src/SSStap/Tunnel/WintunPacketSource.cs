// WintunPacketSource: adapts WintunSession to the IPacketSource interface used by TunnelEngine.

using System.Runtime.InteropServices;
using SSStap.Native;

namespace SSStap.Tunnel;

/// <summary>
/// Wraps a <see cref="WintunSession"/> as an <see cref="IPacketSource"/> for use by <see cref="TunnelEngine"/>.
/// Receive blocks on WintunGetReadWaitEvent (100 ms slices) so cancellation is checked regularly.
/// Send writes directly to the Wintun ring buffer.
/// </summary>
public sealed partial class WintunPacketSource : IPacketSource
{
    private readonly WintunSession _session;

    public WintunPacketSource(WintunSession session)
    {
        _session = session;
    }

    /// <inheritdoc/>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        // Offload to thread-pool so we don't stall the async scheduler while blocking on the event.
        return new ValueTask<int>(Task.Run(() => BlockingReceive(buffer, ct), ct));
    }

    private int BlockingReceive(Memory<byte> buffer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var packet = _session.ReceivePacket();
            if (packet != null)
            {
                using (packet)
                {
                    var data = packet.Data;
                    if (data.Length > buffer.Length)
                        return 0; // oversized — skip
                    data.CopyTo(buffer.Span);
                    return data.Length;
                }
            }

            // No packet in ring buffer — wait up to 100 ms on the kernel event, then re-check cancellation.
            uint result = WaitForSingleObject(_session.ReadWaitEvent, 100);
        }

        return 0;
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
    {
        _session.SendPacket(packet.Span);
        return ValueTask.CompletedTask;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
}
