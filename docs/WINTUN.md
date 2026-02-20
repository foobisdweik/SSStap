# Wintun Integration (Phase 2)

SSStap uses [Wintun](https://www.wintun.net/) as its TUN adapter—a Layer 3 virtual network interface for Windows. This document describes the API surface, deployment, and integration.

## Overview

- **Wintun.dll**: Native driver library; P/Invoke wrapper in `SSStap.Native.Wintun`
- **WintunSession**: High-level class that encapsulates create, read, write, destroy
- **AdapterSetup**: IP assignment via `iphlpapi.dll`

## Wintun DLL Placement

Place `wintun.dll` alongside the SSStap executable. Use the architecture-specific DLL from the [Wintun 0.14.1](https://www.wintun.net/builds/wintun-0.14.1.zip) archive:

| Architecture | DLL Path | Notes |
|-------------|----------|-------|
| x64 (AMD64) | `wintun/bin/amd64/wintun.dll` | Default for 64-bit builds |
| x86        | `wintun/bin/x86/wintun.dll`  | 32-bit builds |
| ARM64      | `wintun/bin/arm64/wintun.dll`| ARM64 Windows |
| ARM32      | `wintun/bin/arm/wintun.dll`  | ARM32 Windows |

**Recommended layout:**

```
SSStap/
├── SSStap.exe
├── wintun.dll          ← x64: copy from wintun/bin/amd64/
└── (other deps)
```

For multi-arch or AnyCPU, place the correct DLL in the output directory per build target. The .NET runtime loads `wintun.dll` from the application directory by default.

**Build step** (add to `.csproj` or post-build):

```xml
<ItemGroup>
  <None Include="path\to\wintun-x64\wintun.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>wintun.dll</Link>
  </None>
</ItemGroup>
```

## Wintun API Surface (P/Invoke)

| Function | Purpose |
|----------|---------|
| `WintunCreateAdapter(name, tunnelType, requestedGuid)` | Create new adapter. Pass `nint.Zero` for random GUID. |
| `WintunOpenAdapter(name)` | Open existing adapter by name |
| `WintunCloseAdapter(adapter)` | Close and remove adapter (if created) |
| `WintunDeleteDriver()` | Unload driver when no adapters in use |
| `WintunGetAdapterLuid(adapter, out luid)` | Get NET_LUID for routing/iphlpapi |
| `WintunGetRunningDriverVersion()` | Driver version; 0 if not loaded |
| `WintunSetLogger(callback)` | Optional diagnostic logger |
| `WintunStartSession(adapter, capacity)` | Start session; capacity 0x20000–0x4000000, power of 2 |
| `WintunEndSession(session)` | End session |
| `WintunGetReadWaitEvent(session)` | Event for blocking read (do not CloseHandle) |
| `WintunReceivePacket(session, out size)` | Get packet pointer; null = ERROR_NO_MORE_ITEMS |
| `WintunReleaseReceivePacket(session, packet)` | Release receive buffer |
| `WintunAllocateSendPacket(session, size)` | Allocate send buffer |
| `WintunSendPacket(session, packet)` | Send and release buffer |

**Constants:**

- `MinRingCapacity` = 0x20000 (128 KiB)
- `MaxRingCapacity` = 0x4000000 (64 MiB)
- `MaxIpPacketSize` = 0xFFFF (65,535 bytes)
- `DefaultRingCapacity` = 0x400000 (4 MiB)

**Error codes:**

- `ERROR_NO_MORE_ITEMS` (259): Receive buffer empty
- `ERROR_HANDLE_EOF` (38): Adapter terminating
- `ERROR_INVALID_DATA` (13): Buffer corrupt
- `ERROR_BUFFER_OVERFLOW` (111): Send buffer full

## iphlpapi.dll / Routing APIs

### IP Assignment

| API | Source | Purpose |
|-----|--------|---------|
| `ConvertInterfaceLuidToIndex` | iphlpapi.dll | Convert NET_LUID → IfIndex for AddIPAddress |
| `AddIPAddress` | iphlpapi.dll | Add non-persistent IPv4 address to adapter |
| `DeleteIPAddress` | iphlpapi.dll | Remove address (use NTEContext from AddIPAddress) |

**Requirements:** Administrator privileges.

**Example:** Set `10.10.10.1/24` on the Wintun adapter:

```csharp
var session = WintunSession.Create("SSStap", "Wintun");
session.SetAdapterIp(IPAddress.Parse("10.10.10.1"), IPAddress.Parse("255.255.255.0"));
```

### netsh Alternative (Persistent Config)

For persistent IP configuration ( survives reboot), use netsh:

```batch
netsh interface ip set address name="SSStap" static 10.10.10.1 255.255.255.0
```

**Note:** The adapter must exist (session started) before netsh can configure it. Run as Administrator.

For SSStap, non-persistent `AddIPAddress` is typically sufficient since the adapter is created on Connect and destroyed on Disconnect.

## WintunSession Usage

```csharp
// Create or open adapter, start session
using var session = WintunSession.Create("SSStap", "Wintun");
if (session is null)
    throw new InvalidOperationException("Failed to create Wintun session");

// Set adapter IP
session.SetAdapterIp(IPAddress.Parse("10.10.10.1"), IPAddress.Parse("255.255.255.0"));

// Read loop (with optional wait)
while (running)
{
    using var packet = session.ReceivePacket();
    if (packet is null)
    {
        // Buffer empty — wait for ReadWaitEvent
        WaitForSingleObject(session.ReadWaitEvent, INFINITE);
        continue;
    }
    ProcessPacket(packet.Data);
}

// Send response
session.SendPacket(responseBytes);
```

## Connection Bootstrap (from Plan)

On **Connect**:

1. Create Wintun adapter with `WintunSession.Create("SSStap", "Wintun")`
2. Set adapter IP via `SetAdapterIp(10.10.10.1, 255.255.255.0)`
3. Add routes via iphlpapi (Phase 3)
4. Start DNS/routing multiplexer loop

On **Disconnect**:

1. Stop multiplexer
2. Call `session.Dispose()` — releases IP, ends session, closes adapter

## References

- [Wintun](https://www.wintun.net/)
- [Wintun API (git.zx2c4.com)](https://git.zx2c4.com/wintun/about/)
- [WireGuard/wintun](https://github.com/WireGuard/wintun)
- [AddIPAddress (Microsoft)](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-addipaddress)
- [ConvertInterfaceLuidToIndex (Microsoft)](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-convertinterfaceluidtoindex)
