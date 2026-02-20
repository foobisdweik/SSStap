// WintunSession: high-level wrapper for create/read/write/destroy lifecycle

using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SSStap.Native;

/// <summary>
/// Encapsulates Wintun adapter and session lifecycle: create, read packets, write packets, destroy.
/// </summary>
public sealed class WintunSession : IDisposable
{
    private Wintun.AdapterHandle _adapter;
    private Wintun.SessionHandle _session;
    private nint _readWaitEvent;
    private AdapterSetup.IpAddressContext? _ipContext;
    private bool _disposed;

    /// <summary>Adapter name (as shown in Network Connections).</summary>
    public string AdapterName { get; }

    /// <summary>Adapter LUID for routing/iphlpapi.</summary>
    public Wintun.NetLuid Luid { get; private set; }

    /// <summary>Handle for WaitForSingleObject when no packets are available.</summary>
    public nint ReadWaitEvent => _readWaitEvent;

    /// <summary>
    /// Creates and starts a Wintun session.
    /// </summary>
    /// <param name="poolName">Adapter pool name (e.g. "SSStap"). Creates adapter if missing.</param>
    /// <param name="tunnelType">Tunnel type (e.g. "Wintun").</param>
    /// <param name="ringCapacity">Ring buffer size. Default 4 MiB. Must be power of 2.</param>
    /// <returns>Started session, or null on failure.</returns>
    public static WintunSession? Create(string poolName = "SSStap", string tunnelType = "Wintun",
        uint ringCapacity = Wintun.DefaultRingCapacity)
    {
        if (string.IsNullOrWhiteSpace(poolName) || string.IsNullOrWhiteSpace(tunnelType))
            return null;

        ringCapacity = Math.Clamp(ringCapacity, Wintun.MinRingCapacity, Wintun.MaxRingCapacity);
        if (BitOperations.PopCount(ringCapacity) != 1)
            ringCapacity = 0x400000; // ensure power of 2

        // Try open first, then create if not found
        var adapter = Wintun.WintunOpenAdapter(poolName);
        if (adapter.IsInvalid)
        {
            adapter = Wintun.WintunCreateAdapter(poolName, tunnelType, nint.Zero);
            if (adapter.IsInvalid)
                return null;
        }

        var session = Wintun.WintunStartSession(adapter, ringCapacity);
        if (session.IsInvalid)
        {
            // Existing adapter may be in use by another process (WireGuard, leftover session).
            // Retry with a fresh adapter using a unique name.
            Wintun.WintunCloseAdapter(adapter);
            var uniqueName = $"{poolName}-{Guid.NewGuid():N}"[..Math.Min(255, poolName.Length + 33)];
            adapter = Wintun.WintunCreateAdapter(uniqueName, tunnelType, nint.Zero);
            if (adapter.IsInvalid)
                return null;
            session = Wintun.WintunStartSession(adapter, ringCapacity);
            if (session.IsInvalid)
            {
                Wintun.WintunCloseAdapter(adapter);
                return null;
            }
            poolName = uniqueName; // use the actual adapter name for the session
        }

        nint readWaitEvent = Wintun.WintunGetReadWaitEvent(session);
        if (readWaitEvent == nint.Zero)
        {
            Wintun.WintunEndSession(session);
            Wintun.WintunCloseAdapter(adapter);
            return null;
        }

        Wintun.WintunGetAdapterLuid(adapter, out var luid);

        return new WintunSession(adapter, session, readWaitEvent, poolName, luid);
    }

    private WintunSession(Wintun.AdapterHandle adapter, Wintun.SessionHandle session,
        nint readWaitEvent, string adapterName, Wintun.NetLuid luid)
    {
        _adapter = adapter;
        _session = session;
        _readWaitEvent = readWaitEvent;
        AdapterName = adapterName;
        Luid = luid;
    }

    /// <summary>
    /// Assigns an IPv4 address to the adapter (e.g. 10.10.10.1/24).
    /// Requires admin. Call before or after Create.
    /// </summary>
    public bool SetAdapterIp(IPAddress address, IPAddress subnetMask)
    {
        var ctx = AdapterSetup.SetAdapterIp(address, subnetMask, Luid);
        if (ctx == null)
            return false;
        _ipContext?.Delete();
        _ipContext = ctx;
        return true;
    }

    /// <summary>
    /// Reads one raw IP packet from the ring buffer.
    /// </summary>
    /// <returns>Packet wrapper with Data span; dispose when done. Null if buffer empty or session ended.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReceivedPacket? ReceivePacket()
    {
        if (_disposed)
            return null;

        nint ptr = Wintun.WintunReceivePacket(_session, out uint size);
        if (ptr == nint.Zero)
        {
            int err = Marshal.GetLastPInvokeError();
            return null; // ERROR_NO_MORE_ITEMS, ERROR_HANDLE_EOF, ERROR_INVALID_DATA
        }

        return new ReceivedPacket(ptr, _session, (int)size);
    }

    /// <summary>
    /// Wrapper for a received packet. Must be disposed (or use using) to release the ring buffer slot.
    /// </summary>
    public sealed class ReceivedPacket : IDisposable
    {
        private nint _ptr;
        private readonly Wintun.SessionHandle _session;
        private readonly int _length;

        internal ReceivedPacket(nint ptr, Wintun.SessionHandle session, int length)
        {
            _ptr = ptr;
            _session = session;
            _length = length;
        }

        public ReadOnlySpan<byte> Data
        {
            get
            {
                if (_ptr == nint.Zero)
                    return default;
                unsafe
                {
                    return new ReadOnlySpan<byte>((void*)_ptr, _length);
                }
            }
        }

        public void Dispose()
        {
            if (_ptr != nint.Zero)
            {
                Wintun.WintunReleaseReceivePacket(_session, _ptr);
                _ptr = nint.Zero;
            }
        }
    }

    /// <summary>
    /// Sends a raw IP packet.
    /// </summary>
    /// <param name="data">Packet bytes (IPv4 or IPv6, max 65535 bytes).</param>
    /// <returns>True if sent, false if buffer full (ERROR_BUFFER_OVERFLOW) or session ended.</returns>
    public bool SendPacket(ReadOnlySpan<byte> data)
    {
        if (_disposed || data.Length == 0 || data.Length > Wintun.MaxIpPacketSize)
            return false;

        nint ptr = Wintun.WintunAllocateSendPacket(_session, (uint)data.Length);
        if (ptr == nint.Zero)
            return false;

        unsafe
        {
            data.CopyTo(new Span<byte>((void*)ptr, data.Length));
        }
        Wintun.WintunSendPacket(_session, ptr);
        return true;
    }

    /// <summary>
    /// Ends the session and closes the adapter.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _ipContext?.Delete();
        _ipContext = null;

        if (!_session.IsInvalid)
        {
            Wintun.WintunEndSession(_session);
            _session = default;
        }
        if (!_adapter.IsInvalid)
        {
            Wintun.WintunCloseAdapter(_adapter);
            _adapter = default;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
