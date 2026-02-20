# SSStap Installer

Wintun.dll (0.14.1, amd64) is downloaded from [wintun.net](https://www.wintun.net/) and bundled automatically.

## Build the installer

From the repo root:

```powershell
.\scripts\build-release.ps1
```

**With Inno Setup 6 installed:** produces `dist/SSStap-Setup-0.1.0.exe`

**Without Inno Setup:** produces `dist/SSStap-0.1.0-win-x64.zip` (portable)

Install Inno Setup: `winget install JRSoftware.InnoSetup`

## Manual compilation

1. Install [Inno Setup 6](https://jrsoftware.org/isdl.php)
2. Open `installer/SSStap.iss` in Inno Setup
3. Build → Compile (Ctrl+F9)
4. Output: `dist/SSStap-Setup-0.1.0.exe`
