// SPDX-License-Identifier: MIT
// P/Invoke wrapper for wintun.dll (https://www.wintun.net/)
// API reference: https://git.zx2c4.com/wintun/about/

using System.Runtime.InteropServices;

namespace SSStap.Native;

/// <summary>
/// P/Invoke declarations for wintun.dll. Load dynamically to handle missing DLL.
/// </summary>
public static partial class Wintun
{
    private const string DllName = "wintun";

    // Constants from wintun.h
    public const uint MinRingCapacity = 0x20000;   // 128 KiB
    public const uint MaxRingCapacity = 0x4000000;  // 64 MiB
    public const uint MaxIpPacketSize = 0xFFFF;    // 65,535 bytes
    public const uint DefaultRingCapacity = 0x400000; // 4 MiB (common default)

    // Windows error codes from Wintun
    public const int ErrorNoMoreItems = 259;       // ERROR_NO_MORE_ITEMS
    public const int ErrorHandleEof = 38;          // ERROR_HANDLE_EOF
    public const int ErrorInvalidData = 13;       // ERROR_INVALID_DATA
    public const int ErrorBufferOverflow = 111;   // ERROR_BUFFER_OVERFLOW

    public const int MaxAdapterName = 256;

    /// <summary>Opaque handle to a Wintun adapter.</summary>
    public readonly struct AdapterHandle : IEquatable<AdapterHandle>
    {
        public readonly nint Value;
        public AdapterHandle(nint value) => Value = value;
        public bool IsInvalid => Value == nint.Zero;
        public static bool operator ==(AdapterHandle a, AdapterHandle b) => a.Value == b.Value;
        public static bool operator !=(AdapterHandle a, AdapterHandle b) => a.Value != b.Value;
        public bool Equals(AdapterHandle other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AdapterHandle h && Equals(h);
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>Opaque handle to a Wintun session.</summary>
    public readonly struct SessionHandle : IEquatable<SessionHandle>
    {
        public readonly nint Value;
        public SessionHandle(nint value) => Value = value;
        public bool IsInvalid => Value == nint.Zero;
        public static bool operator ==(SessionHandle a, SessionHandle b) => a.Value == b.Value;
        public static bool operator !=(SessionHandle a, SessionHandle b) => a.Value != b.Value;
        public bool Equals(SessionHandle other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is SessionHandle h && Equals(h);
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>64-bit network interface LUID (Locally Unique IDentifier).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct NetLuid
    {
        [FieldOffset(0)]
        public ulong Value;

        [FieldOffset(0)]
        public LuidInfo Info;

        [StructLayout(LayoutKind.Sequential)]
        public struct LuidInfo
        {
            public ulong Reserved;      // 24 bits
            public ulong NetLuidIndex;  // 24 bits
            public ulong IfType;        // 16 bits
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WintunLoggerCallback(uint level, ulong timestamp, [MarshalAs(UnmanagedType.LPWStr)] string message);

    /// <summary>
    /// Creates a new Wintun adapter.
    /// </summary>
    /// <param name="name">Adapter name (max MAX_ADAPTER_NAME-1 chars).</param>
    /// <param name="tunnelType">Tunnel type (e.g. "Wintun").</param>
    /// <param name="requestedGuid">Optional GUID for deterministic NLA. Pass IntPtr.Zero for random.</param>
    /// <returns>Adapter handle or invalid on failure. Call GetLastError for details.</returns>
    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf16)]
    public static partial AdapterHandle WintunCreateAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        nint requestedGuid);

    /// <summary>
    /// Opens an existing Wintun adapter by name.
    /// </summary>
    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf16)]
    public static partial AdapterHandle WintunOpenAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name);

    /// <summary>
    /// Closes and optionally removes the adapter (if created with WintunCreateAdapter).
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunCloseAdapter(AdapterHandle adapter);

    /// <summary>
    /// Deletes the Wintun driver when no adapters are in use.
    /// </summary>
    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WintunDeleteDriver();

    /// <summary>
    /// Returns the LUID of the adapter (for routing/IP config).
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunGetAdapterLuid(AdapterHandle adapter, out NetLuid luid);

    /// <summary>
    /// Gets the running Wintun driver version. Returns 0 if not loaded.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial uint WintunGetRunningDriverVersion();

    /// <summary>
    /// Sets the global logger callback. Pass null to disable.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunSetLogger(nint loggerCallback);

    /// <summary>
    /// Starts a session on the adapter.
    /// </summary>
    /// <param name="adapter">Adapter handle.</param>
    /// <param name="capacity">Ring capacity (power of 2, between Min and Max).</param>
    /// <returns>Session handle or invalid on failure.</returns>
    [LibraryImport(DllName)]
    public static partial SessionHandle WintunStartSession(AdapterHandle adapter, uint capacity);

    /// <summary>
    /// Ends the session and releases resources.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunEndSession(SessionHandle session);

    /// <summary>
    /// Gets the read-wait event for blocking until packets are available.
    /// Do NOT CloseHandle this event.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial nint WintunGetReadWaitEvent(SessionHandle session);

    /// <summary>
    /// Receives one packet. Call WintunReleaseReceivePacket when done.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packetSize">Receives the packet size.</param>
    /// <returns>Pointer to packet data or null (ERROR_NO_MORE_ITEMS = buffer exhausted).</returns>
    [LibraryImport(DllName)]
    public static partial nint WintunReceivePacket(SessionHandle session, out uint packetSize);

    /// <summary>
    /// Releases the receive packet buffer.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunReleaseReceivePacket(SessionHandle session, nint packet);

    /// <summary>
    /// Allocates space for a send packet. Fill it, then call WintunSendPacket.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packetSize">Size of packet (max MaxIpPacketSize).</param>
    /// <returns>Pointer to buffer or null (ERROR_BUFFER_OVERFLOW = ring full).</returns>
    [LibraryImport(DllName)]
    public static partial nint WintunAllocateSendPacket(SessionHandle session, uint packetSize);

    /// <summary>
    /// Sends the packet and releases the send buffer.
    /// </summary>
    [LibraryImport(DllName)]
    public static partial void WintunSendPacket(SessionHandle session, nint packet);
}
