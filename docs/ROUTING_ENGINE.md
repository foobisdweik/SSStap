# SSStap Phase 3: Routing Engine & QUIC

## Overview

The routing engine implements packet processing and forwarding for traffic that is routed through the Wintun TUN adapter. It parses raw IP packets, applies route-table-based and packet-level filtering, and forwards traffic via SOCKS5 (TCP/UDP) or QUIC (stub).

## Architecture

```
[Wintun] → IPacketSource → TunnelEngine
                              ├── IpPacketParser (L3/L4)
                              ├── RouteManager (filter: ShouldForwardViaProxy)
                              ├── TCP → SOCKS5 CONNECT
                              ├── UDP → SOCKS5 UDP ASSOCIATE
                              └── QUIC → libcurl HTTP/3 (stub)
```

## L3/L4 Parser

**Location:** `PacketProcessing/IpPacketParser.cs`, `ParsedPacket.cs`

**Approach:** Minimal managed parser. No external dependencies (lwIP, etc.).

- **Rationale:** lwIP is C-based; integrating would require P/Invoke or native DLL. For routing decisions (src/dst IP/port, protocol) and payload extraction, a minimal parser is sufficient.
- **Parsed fields:** IP version, protocol (TCP=6, UDP=17), total length, header length, source/dest address, source/dest port, payload offset/length.
- **RFCs:** 791 (IP), 793 (TCP), 768 (UDP).

**Alternative:** For full TCP reassembly or complex scenarios, consider integrating lwIP as a native library.

## Route Table API

**Location:** `Routing/RouteTableApi.cs`

**P/Invoke:** `iphlpapi.dll`
- `InitializeIpForwardEntry` – initialize `MIB_IPFORWARD_ROW2`
- `CreateIpForwardEntry2` – add route
- `DeleteIpForwardEntry2` – remove route

**Route metric and interface index:**

- **Metric:** Lower = higher priority. `RouteManager.DefaultRouteMetric = 1` so proxy routes override the default route.
- **Interface index:** From `config.ini`:
  - `tap_connection_index` – Wintun adapter (traffic to proxy)
  - `local_connection_index` – Physical adapter (for SkipChina: China routes go here)

**SkipChina mode:** China CIDRs get routes to the physical adapter (metric 0). Default route `0.0.0.0/0` goes to Wintun (metric 1). China traffic uses the physical adapter; other traffic uses the proxy.

## Split Routing Rules

**Files:** `rules/China-IP-only.rules`, `rules/Skip-all-China-IP.rules`

**Format:** First line is a comment (`#...`). Remaining lines are CIDRs (e.g. `1.0.1.0/24`).

**Modes:**
- **Global:** Single route `0.0.0.0/0` → Wintun.
- **China-only:** One route per China CIDR → Wintun. Uses `China-IP-only.rules`.
- **Skip-China:** China CIDRs → physical adapter; `0.0.0.0/0` → Wintun. Uses `Skip-all-China-IP.rules`. Requires `physicalAdapterIndex`.

**ChinaIpRules:** Loads rules, exposes `Contains(IPAddress)` and `GetCidrs()`.

## Multiplexer Design

**Location:** `Tunnel/TunnelEngine.cs` (also referred to as PacketMultiplexer)

| Protocol | Handler | Notes |
|----------|---------|-------|
| TCP | SOCKS5 CONNECT | Per-connection state; forwards payload. Full TCP reassembly not yet implemented. |
| UDP | SOCKS5 UDP ASSOCIATE | Encapsulates in SOCKS5 UDP envelope. Response path TBD. |
| QUIC | Stub | `QuicHandler.cs` – future libcurl HTTP/3. |

**Packet flow:**
1. `IPacketSource.ReceiveAsync` yields a raw packet.
2. `IpPacketParser.TryParse` → `ParsedPacket`.
3. `RouteManager.ShouldForwardViaProxy(dest)` – skip if packet should not go to proxy.
4. Dispatch to TCP/UDP handler.

## Limitations & TODO

- **TCP:** No reassembly. Per-segment forwarding can reorder data. Reassembly or lwIP integration recommended for production.
- **TCP response:** Relay from proxy must build full IP+TCP packets before writing to Wintun. Not yet implemented.
- **UDP:** Send path (client → proxy) scaffolded. Receive path (proxy → client) and UDP socket handling TBD.
- **SkipChina:** Packets for China IPs still reach Wintun with the current routing. `ShouldForwardViaProxy` filters them, but they are dropped instead of being forwarded to the physical interface. Full SkipChina would require reinjection onto the physical adapter.

## QUIC Stub

**Location:** `Tunnel/QuicHandler.cs`

Placeholder for libcurl HTTP/3 (ngtcp2, nghttp3). Plan: build libcurl with QUIC support, use for selected traffic.
