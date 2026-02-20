# SSStap

Modern replacement for SSTap 1.0.9.7 — a Windows tunneling client that routes system-wide traffic through upstream proxies (SOCKS5, Shadowsocks, SSR).

## Structure

```
SSStap/
├── assets/          # Config templates, rules, Unbound templates (copied to build output)
├── docs/            # Plan, architecture, bugtest reports
├── reference/       # Legacy SSTap 1.0.9.7 artifacts (reverse-engineering reference)
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

Place **wintun.dll** (amd64 from [wintun.net](https://www.wintun.net/)) in `src/SSStap/bin/Debug/net8.0-windows/` for Connect to work. Run as Administrator for full functionality.

## Tests

```bash
cd src
dotnet test
```

## See Also

- [docs/sstap_reverse_engineering_plan.md](docs/sstap_reverse_engineering_plan.md)
- [docs/BUGTEST_REPORT.md](docs/BUGTEST_REPORT.md)
