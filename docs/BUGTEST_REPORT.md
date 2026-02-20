# SSStap Bugtest Report

## Summary

Automated tests added and manual verification performed. **16/16 tests pass.** Several bugs were found and fixed.

---

## Bugs Fixed

### 1. **AddFromLinkDialog — SSR Protocol/Obfs Lost**
- **Issue:** Parsing an `ssr://` link correctly populated Protocol (e.g. `auth_aes128_md5`) and Obfs (e.g. `tls1.2_ticket_auth`), but clicking Add overwrote them with `"origin"` and `"plain"`.
- **Fix:** Store the full parsed `ProxyConfig` in `_parsedConfig` and use its `Protocol`, `Obfs`, `ObfsParam`, `ProtocolParam` when building `ResultConfig`.

### 2. **Wintun — Outdated DLL Handling**
- **Issue:** `wintun.dll` from 2021 lacks the `WintunGetAdapterLuid` export (added in later versions). Connect would throw `EntryPointNotFoundException`.
- **Fix:** Catch `EntryPointNotFoundException` and show: *"wintun.dll is outdated. Download amd64 build from https://www.wintun.net/"*

### 3. **UnboundLauncher — Missing `System.IO`**
- **Issue:** `Path`, `File`, `Directory` not in scope.
- **Fix:** Added `using System.IO;` to `UnboundLauncher.cs`.

### 4. **ConfigService — Testability**
- **Issue:** Tests could not override config directory.
- **Fix:** Added optional constructor parameter `configDirectoryOverride` for tests.

---

## Tests Added

| Suite | Tests | Coverage |
|-------|-------|----------|
| **ConfigServiceTests** | 6 | Load/save proxylist.json, config.ini; legacy format; round-trip |
| **ProxyLinkParserTests** | 8 | ss://, ssr://, SIP002, invalid input, null |
| **WintunTests** | 2 | Driver version, session create (handles old DLL) |

---

## Wintun DLL Version

Your `wintun.dll` (Oct 2021, 427 KB) does **not** export `WintunGetAdapterLuid`. For full Connect support:

1. Download the **latest amd64** build from https://www.wintun.net/
2. Replace `src/SSStap/bin/Debug/net8.0-windows/wintun.dll`
3. Restart the app and Connect (requires Administrator for adapter IP assignment)

---

## Connect Flow (Current)

1. User selects proxy → clicks **Connect**
2. `WintunSession.Create()` — creates/opens adapter, starts session
3. `SetAdapterIp("10.10.10.1", "255.255.255.0")` — requires Admin
4. Session stored; status set to *"Connected (Wintun ready)"*
5. On **Disconnect** or window close, session is disposed

**Note:** TunnelEngine (packet forwarding) and routing are not yet wired to the UI; Connect only brings up the Wintun adapter.

---

## Recommendations

1. **Update wintun.dll** — Use the current build from wintun.net.
2. **Run as Administrator** — For Connect to succeed with `SetAdapterIp`.
3. **Close SSStap before rebuilding** — Avoid exe lock during `dotnet build`.
