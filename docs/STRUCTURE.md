# Repository Structure

## Layout

```
SSStap/
├── .gitignore
├── LICENSE
├── README.md
│
├── assets/                    # App data (copied to output)
│   ├── config/
│   │   ├── proxylist.json
│   │   ├── config.ini
│   │   └── ...
│   ├── rules/
│   │   ├── China-IP-only.rules
│   │   ├── Skip-all-China-IP.rules
│   │   └── ...
│   └── unbound/
│       ├── template-service.conf
│       └── forward-zone/
│           └── template.china-list.conf
│
├── docs/
│   ├── BUGTEST_REPORT.md
│   ├── ROUTING_ENGINE.md
│   ├── STRUCTURE.md
│   ├── WINTUN.md
│   └── sstap_reverse_engineering_plan.md
│
├── reference/                 # SSTap 1.0.9.7 artifacts
│   ├── Changelog.txt
│   ├── readme.txt
│   ├── privoxy.conf
│   ├── lang/
│   ├── skins/
│   └── tap-driver/
│
└── src/
    ├── SSStap.sln
    ├── SSStap/                # Main app
    │   ├── App.xaml
    │   ├── MainWindow.xaml
    │   ├── Dialogs/
    │   ├── Dns/
    │   ├── Http/
    │   ├── Models/
    │   ├── Native/            # Wintun P/Invoke
    │   ├── PacketProcessing/
    │   ├── Routing/
    │   ├── Services/
    │   ├── Tunnel/
    │   └── ViewModels/
    └── SSStap.Tests/
```

## Path Resolution

- **Assets** are referenced from `SSStap.csproj` via `../../assets/...` and copied to output as `config/`, `rules/`, `unbound/`.
- **ConfigService** resolves `config/` at runtime from `AppContext.BaseDirectory` (output folder).
- **UnboundLauncher** uses templates from `unbound/` in the output directory.
