param(
    [string]$SourcePath = ".\source",
    [string]$OutputPath = ".\preheated_context.edn"
)

# The 15-language extension whitelist from MASTER_CLAUDE.md
$Extensions = @("*.hcl", "*.tf", "*.rs", "*.ts", "*.tsx", "*.lua", "*.go", "*.sh", 
                "*.bash", "Makefile", "*.c", "*.h", "*.cpp", "*.hpp", "*.cs", 
                "*.js", "*.jsx", "*.rb", "*.kt", "*.py", "*.swift")

$results = @()
$files = Get-ChildItem -Path $SourcePath -Include $Extensions -Recurse -File

Write-Host "Preheating $($files.Count) files..." -ForegroundColor Cyan

foreach ($file in $files) {
    $relative    = Resolve-Path $file.FullName -Relative
    $rawContent  = Get-Content $file.FullName -Raw
    
    # Escape quotes and backslashes for valid EDN string embedding
    $escaped = $rawContent -replace '\\', '\\\\' -replace '"', '\"'
    
    # Create the EDN map entry
    $ednBlock = "{:file `"$relative`" :content `"$escaped`"}"
    $results += $ednBlock
}

# Wrap all files into a single EDN vector
"[$($results -join "`n ")]" | Set-Content -Path $OutputPath -Encoding utf8

Write-Host "[✓] Preheating complete. Context saved to $OutputPath" -ForegroundColor Green