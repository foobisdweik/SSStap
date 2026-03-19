# Build SSStap release and create installer
# Requires: .NET 8 SDK, Inno Setup 6 (optional - for installer .exe)
# Output: dist/SSStap-Setup-0.1.0.exe

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir = Join-Path $RepoRoot "src"
$DistDir = Join-Path $RepoRoot "dist"
$NativeDir = Join-Path $RepoRoot "assets\native"

Write-Host "== Building SSStap Release ==" -ForegroundColor Cyan

# 1. Fetch wintun.dll into assets/native/ before building so the csproj Content item
#    copies it to every output (Debug, Release, publish).
$WintunDll = Join-Path $NativeDir "wintun.dll"
if (-not (Test-Path $WintunDll)) {
    Write-Host "Downloading wintun.dll 0.14.1 (amd64)..." -ForegroundColor Cyan
    $WintunUrl  = "https://www.wintun.net/builds/wintun-0.14.1.zip"
    $WintunZip  = Join-Path $env:TEMP "wintun-0.14.1.zip"
    $ExtractDir = Join-Path $env:TEMP "wintun-extract"
    try {
        Invoke-WebRequest -Uri $WintunUrl -OutFile $WintunZip -UseBasicParsing -TimeoutSec 120
        Expand-Archive -Path $WintunZip -DestinationPath $ExtractDir -Force
        $null = New-Item -ItemType Directory -Force -Path $NativeDir
        Copy-Item (Join-Path $ExtractDir "wintun\bin\amd64\wintun.dll") -Destination $WintunDll -Force
        Write-Host "wintun.dll saved to assets/native/" -ForegroundColor Green
    } finally {
        Remove-Item $WintunZip, $ExtractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "wintun.dll already present in assets/native/ — skipping download." -ForegroundColor DarkGray
}

# 2. Publish (csproj Content item copies wintun.dll automatically)
Push-Location $SrcDir
try {
    dotnet publish SSStap/SSStap.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true -p:WINTUN_SKIP_DOWNLOAD=1
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    Write-Host "Publish complete." -ForegroundColor Green
} finally {
    Pop-Location
}

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
    if (Test-Path $p) { $Iscc = $p; break }
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
    # Fallback: zip the publish output
    $PublishDir = Join-Path $SrcDir "SSStap\bin\Release\net8.0-windows\win-x64\publish"
    $ZipPath = Join-Path $DistDir "SSStap-0.1.0-win-x64.zip"
    if (Test-Path $PublishDir) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
        Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
        Write-Host "Created portable zip: $ZipPath" -ForegroundColor Green
    }
}
# Inside scripts/build-release.ps1
Write-Host "--- Building Hybrid SSStap Binary ---" -ForegroundColor Cyan

# 1. Compile the high-performance native core
rtk-g dotnet publish -c Release -r win-x64 --self-contained

$TargetExe = "src\SSStap\bin\Release\net8.0-windows\win-x64\publish\SSStap.exe"
$PayloadDir = "C:\Users\Foobis\Source\Source-Mirror\SSStap"

# 2. Grab the "Layman-Readable" EDN from the Mirror
if (Test-Path $PayloadDir) {
    $ednBlocks = Get-ChildItem -Path $PayloadDir -Recurse -Filter "*.edn" | Get-Content -Raw
    $FinalPayload = "[$($ednBlocks -join "`n ")]"
    
    # 3. Staple the EDN to the tail of the .exe
    $Marker = "`n___EDN_PAYLOAD___`n"
    Add-Content -Path $TargetExe -Value $Marker -NoNewline
    Add-Content -Path $TargetExe -Value $FinalPayload -NoNewline
    
    Write-Host "[✓] EDN Payload Stapled to Native Binary." -ForegroundColor Green
}