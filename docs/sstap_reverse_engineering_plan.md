# SSStap Build Guide (Target: Agent Composer 1.5 MAX)

**Project Name:** SSStap
**Objective:** Build a modernized, GUI-driven replacement for SSTap 1.0.9.7. The application will serve as a robust Windows tunneling client designed to route system-wide traffic through an upstream proxy, specifically optimized to interface with a custom iOS SOCKS5 proxy server.
**Key Upgrades:** Wintun adapter replacement, libcurl/libsodium modernization, and integration of TCP, UDP, and full QUIC tunneling support.

---

## 1. Out-of-Date Libraries & Drivers — Identification & Replacement Matrix

| Legacy Component | Version in SSTap 1.0.9.7 | Status | Modern Replacement | Version | Compatibility Notes |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **TAP-Windows** | 9.00.00.21 (Apr 2016) | Obsolete | **Wintun** | 0.14.1+ | Layer 3 TUN, no kernel-mode NDIS overhead. WireGuard-style. Replaces TAP entirely. |
| **libcurl** | ~7.x (2017-era) | No QUIC | **libcurl** | 8.8+ | Build with `ngtcp2` + `nghttp3`. TLS: OpenSSL 3.5+ or QuicTLS. |
| **libsodiumR** | Legacy fork | Unmaintained | **libsodium** | 1.0.19+ | Drop-in crypto for SS/SSR. Standard API. |
| **libiconv** | 2.x | Old | **libiconv** | 1.17+ (or .NET `Encoding`) | Prefer managed C# conversion where possible. |
| **libintl** | 3.x | Old | — | — | Replace with .NET `System.Globalization` / `CultureInfo`. |
| **Unbound** | Unknown (2017-era) | Outdated | **Unbound** | 1.19.1+ | Use existing `unbound/` templates. Run as managed child process. |
| **Privoxy** | Standalone exe | Heavy | **In-Memory C# Shim** | N/A | SOCKS→HTTP local listener. No external binary. |

### Recommended Coherent Stack (No Conflicts)

| Layer | Component | Version | Source / Notes |
| :--- | :--- | :--- | :--- |
| TUN Adapter | Wintun | 0.14.1 | https://www.wintun.net/ — signed DLLs (AMD64, x86, ARM64) |
| HTTP/QUIC | libcurl | 8.8+ | Build with ngtcp2, nghttp3, OpenSSL 3.5+ |
| Crypto | libsodium | 1.0.19+ | https://libsodium.org/ |
| DNS | Unbound | 1.19.1+ | https://nlnetlabs.nl/projects/unbound/ |
| Locale/Iconv | .NET | — | Use `Encoding`, `CultureInfo` — no native libs |
| HTTP Proxy | C# TcpListener | — | Internal SOCKS5→HTTP proxy on 127.0.0.1 |

---

## 2. Technology Stack & GUI Framework

The application will be built using **C# with WinUI 3** (or WPF fallback) for:
- Native Windows look and interop
- P/Invoke to Wintun, libcurl, libsodium
- Managed child process control (Unbound)

---

## 3. SSTap Workflow Parity — Exact Step Count

### First-Time Setup: 6 Steps (Add Proxy → Connect)

| Step | User Action | UI Element |
| :--- | :--- | :--- |
| 1 | Click **"Add Proxy"** (+) | Main window action button |
| 2 | Choose type from context menu | "Add SOCKS5 Proxy" / "Add SS/SSR Proxy" / "Add from Link" |
| 3 | Fill proxy form (server, port, password, etc.) | Dialog fields |
| 4 | Optionally set remarks / group | Dialog fields |
| 5 | Click **"Add"** (or "Save") | Dialog button |
| 6 | Select proxy from dropdown → Click **"Connect"** | Main window |

### Returning User: 2 Steps

| Step | User Action | UI Element |
| :--- | :--- | :--- |
| 1 | Select proxy from dropdown (already configured) | Proxy Node Dropdown |
| 2 | Click **"Connect"** | Main window |

**Constraint:** The new app must require no more than these steps. No extra wizards, no mandatory "mode" selection before connect (mode can default to last used).

---

## 4. Data Model

### Config Path

`config/proxylist.json` — persisted, editable.

### JSON Schema (Backward-Compatible + Extended)

```json
{
  "configs": [
    {
      "id": "uuid",
      "server": "string",
      "server_port": 9999,
      "server_udp_port": 0,
      "password": "",
      "method": "none|aes-128-gcm|...",
      "protocol": "origin|...",
      "obfs": "plain|...",
      "obfsparam": "",
      "protocolparam": "",
      "remarks": "",
      "group": "Default Group",
      "type": 5,
      "enable_quic": false,
      "username": "",
      "AdditionalRoute": "",
      "enable": true
    }
  ],
  "idInUse": 0
}
```

**Type codes:** `1`=HTTP, `4`=SOCKS4, `5`=SOCKS5, `6`=Shadowsocks, `7`=ShadowsocksR.

### Config.ini (Settings)

Preserve keys from `config/config.ini`: `startup`, `AutomaticallyEstablishConnection`, `AutomaticHideWindow`, `dns_type`, `bGlobalMode`, `last_proxymode_index`, `bReduceTCPDelayedACK`, etc.

---

## 5. Main Interface Layout

| Element | Description |
| :--- | :--- |
| **Proxy Node Dropdown** | Lists configs from `proxylist.json`; `idInUse` = selection |
| **Mode Selector** | Global / China IP Only / Skip China IP / Browser-only |
| **Add Proxy** | Opens context: Add SOCKS5, Add SS/SSR, Add from Link |
| **Connect / Disconnect** | Toggle connection state |
| **System Tray** | Minimize to tray; right-click: Connect, Disconnect, Exit |

---

## 6. Connection Bootstrap Sequence (On Connect)

1. **Validate** — Ensure selected proxy config exists and is reachable (optional ping/test).
2. **Wintun** — Create adapter, assign IP (e.g. `10.10.10.1`), bring up.
3. **Routing** — Add routes via `iphlpapi.dll` per mode (global vs. split).
4. **DNS** — Start Unbound child process with generated `service.conf`.
5. **Multiplexer** — Start loop: read from Wintun ring buffer → L3→L4 parse → SOCKS5/QUIC forward.

On Disconnect: Tear down in reverse order (stop multiplexer, stop Unbound, remove routes, destroy Wintun session).

---

## 7. Architecture & Data Flow

```
[System Apps] → [TCP/UDP] → [Routing Table] → [Wintun Adapter]
                                                    ↓
                                            [L3→L4 Parser]
                                                    ↓
                              ┌─────────────────────┼─────────────────────┐
                              ↓                     ↓                     ↓
                         [TCP SOCKS5]          [UDP ASSOCIATE]        [QUIC/libcurl]
                              ↓                     ↓                     ↓
                         [iOS SOCKS5 Proxy / Upstream]
```

---

## 8. Phase Execution Plan

### Phase 1: Project Scaffolding & GUI Parity
- Create C# solution (WinUI 3 or WPF)
- Main window with Proxy dropdown, Mode selector, Add/Connect/Disconnect
- Add Proxy dialogs: SOCKS5, SS/SSR, From Link (ss://, ssr://)
- Serialize/deserialize `config/proxylist.json` and `config/config.ini`

### Phase 2: Wintun Integration
- `Wintun.cs` P/Invoke wrapper
- Install adapter, set IP, read/write raw packet buffers
- Session lifecycle (create/destroy)

### Phase 3: Routing Engine & QUIC ✓
- L3/L4 parser: Minimal managed parser (`PacketProcessing/IpPacketParser.cs`) — see `docs/ROUTING_ENGINE.md`
- Multiplexer: `TunnelEngine` — TCP→SOCKS5 CONNECT, UDP→UDP ASSOCIATE (stub), QUIC stub
- Route table: `iphlpapi.dll` via `Routing/RouteTableApi.cs`, split routing via `rules/China-IP-only.rules`, `Skip-all-China-IP.rules`

### Phase 4: DNS & HTTP Shim
- Unbound child process with template-based config
- Optional: In-memory SOCKS→HTTP shim for browser-only mode

---

## 9. Parallel Workflow Targets

| Workflow | Phase | Output |
| :--- | :--- | :--- |
| **GUI** | Phase 1 | Solution, MainWindow, Add Proxy dialogs, config bindings |
| **Wintun** | Phase 2 | Wintun.cs, adapter setup, packet I/O |
| **Routing** | Phase 3 | Parser, multiplexer, route API |
| **DNS** | Phase 4 | Unbound launcher, config generator |

These can be developed in parallel where dependencies allow.
