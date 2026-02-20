# SSStap

Windows tunneling client that routes system-wide traffic through upstream proxies (SOCKS5, Shadowsocks, SSR).

## Structure

```
SSStap/
├── assets/          # Config templates, rules, Unbound templates (copied to build output)
├── docs/            # Plan, architecture, bugtest reports
└── src/
    ├── SSStap/      # Main application
    └── SSStap.Tests/
```

## Build & Run

```bash
cd src
dotnet build
dotnet run --project SSStap
```

## Release installer

```powershell
.\scripts\build-release.ps1
```

Produces `dist/SSStap-Setup-0.1.0.exe` (requires [Inno Setup 6](https://jrsoftware.org/isdl.php))  
or `dist/SSStap-0.1.0-win-x64.zip` as fallback.

**Debug:** Place wintun.dll in `bin/Debug/net8.0-windows/` for Connect. **Release/installer:** wintun.dll is bundled automatically. Run as Administrator for full functionality.

## Tests

```bash
cd src
dotnet test
```

## See Also

- [docs/sstap_reverse_engineering_plan.md](docs/sstap_reverse_engineering_plan.md)
- [docs/BUGTEST_REPORT.md](docs/BUGTEST_REPORT.md)
