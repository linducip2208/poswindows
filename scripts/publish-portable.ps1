# Publish portable package (framework-dependent ZIP layout)
param([string]$OutputDir = "dist")
$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;$env:PATH"

Write-Host "=== Publish KasirPro (framework-dependent win-x64) ===" -ForegroundColor Cyan
dotnet publish src/KasirPro.App -c Release -r win-x64 --self-contained false -o "$OutputDir/KasirPro"
if ($LASTEXITCODE -ne 0) { throw "publish app failed" }

Write-Host "=== Publish KasirPro.Keygen (DeveloperTools) ===" -ForegroundColor Cyan
dotnet publish tools/KasirPro.Keygen -c Release -r win-x64 --self-contained false -o "$OutputDir/DeveloperTools/KasirPro.Keygen"
if ($LASTEXITCODE -ne 0) { throw "publish keygen failed" }

Write-Host "=== Preparing runtime folders ===" -ForegroundColor Cyan
foreach ($d in @("$OutputDir\KasirPro\Data", "$OutputDir\KasirPro\Backup", "$OutputDir\KasirPro\Images\Products",
                 "$OutputDir\KasirPro\Exports\Reports", "$OutputDir\KasirPro\Logs", "$OutputDir\KasirPro\Updates")) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

# safety: customer package must never contain key material
foreach ($bad in @("$OutputDir\KasirPro\license.dat", "$OutputDir\KasirPro\Keys", "$OutputDir\KasirPro\DeveloperTools")) {
    if (Test-Path $bad) { Remove-Item $bad -Recurse -Force; Write-Host "removed forbidden: $bad" -ForegroundColor Yellow }
}

Write-Host "PORTABLE OK -> $OutputDir/KasirPro" -ForegroundColor Green
