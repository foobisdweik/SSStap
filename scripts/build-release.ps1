# Build SSStap release and create installer
# Requires: .NET 8 SDK, Inno Setup 6 (optional - for installer .exe)
# Output: dist/SSStap-Setup-0.1.0.exe

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir = Join-Path $RepoRoot "src"
$DistDir = Join-Path $RepoRoot "dist"

Write-Host "== Building SSStap Release ==" -ForegroundColor Cyan

# 1. Publish
Push-Location $SrcDir
try {
    dotnet publish SSStap/SSStap.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    Write-Host "Publish complete." -ForegroundColor Green
} finally {
    Pop-Location
}

# 2. Bundle wintun.dll (official signed build from wintun.net) - always refresh to avoid stale 0.12/etc
$PublishDir = Join-Path $SrcDir "SSStap\bin\Release\net8.0-windows\win-x64\publish"
$WintunUrl = "https://www.wintun.net/builds/wintun-0.14.1.zip"
$WintunZip = Join-Path $env:TEMP "wintun-0.14.1.zip"
Write-Host "Downloading wintun.dll (0.14.1)..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $WintunUrl -OutFile $WintunZip -UseBasicParsing
$ExtractDir = Join-Path $env:TEMP "wintun-extract"
Expand-Archive -Path $WintunZip -DestinationPath $ExtractDir -Force
Copy-Item (Join-Path $ExtractDir "wintun\bin\amd64\wintun.dll") -Destination $PublishDir -Force
Remove-Item $WintunZip -Force -ErrorAction SilentlyContinue
Remove-Item $ExtractDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Bundled wintun.dll" -ForegroundColor Green

# 3. Find Inno Setup compiler
$IsccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6.7\ISCC.exe",
    "C:\Program Files\Inno Setup 6.7\ISCC.exe"
)
$Iscc = $null
foreach ($p in $IsccPaths) {
    if (Test-Path $p) {
        $Iscc = $p
        break
    }
}

# 4. Build installer
$IssPath = Join-Path $RepoRoot "installer\SSStap.iss"
if ($Iscc -and (Test-Path $IssPath)) {
    Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    & $Iscc $IssPath
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }
    $SetupExe = Get-ChildItem -Path $DistDir -Filter "SSStap-Setup-*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($SetupExe) {
        Write-Host "Installer: $($SetupExe.FullName)" -ForegroundColor Green
    }
} else {
    Write-Host ""
    Write-Host "Inno Setup 6 not found. To create the installer:" -ForegroundColor Yellow
    Write-Host "  1. Install: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host "  2. Re-run this script, or open $IssPath in Inno Setup and compile (Ctrl+F9)" -ForegroundColor Yellow
    Write-Host ""
    # Fallback: create zip of published output
    $ZipPath = Join-Path $DistDir "SSStap-0.1.0-win-x64.zip"
    if (Test-Path $PublishDir) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
        Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
        Write-Host "Created portable zip: $ZipPath" -ForegroundColor Green
    }
}
