# Fix Wintun "Timed out waiting for device query" / adapter creation failure
# Root cause: Stale Wintun drivers from previous WireGuard/Tailscale/SSStap uninstalls
# Run as Administrator. Ref: https://github.com/tailscale/tailscale/issues/6461

$ErrorActionPreference = "Stop"

Write-Host "== Wintun driver cleanup ==" -ForegroundColor Cyan
Write-Host ""

$output = pnputil /enum-drivers 2>&1 | Out-String

# Parse blocks: each block has Published Name and Original Name
$blocks = $output -split "(?=Published Name:)"
$found = @()
foreach ($block in $blocks) {
    if ($block -match "wintun|wireguard") {
        if ($block -match "Published Name:\s+(oem\d+\.inf)") {
            $found += $matches[1]
        }
    }
}
$found = $found | Select-Object -Unique

if ($found.Count -eq 0) {
    Write-Host "No Wintun/WireGuard OEM drivers found." -ForegroundColor Green
    exit 0
}

Write-Host "Found: $($found -join ', ')" -ForegroundColor Yellow
foreach ($oem in $found) {
    Write-Host ""
    Write-Host "Removing $oem ..." -ForegroundColor Cyan
    pnputil /delete-driver $oem /uninstall 2>&1
}
Write-Host ""
Write-Host "Done. Restart SSStap." -ForegroundColor Green
