# KasirPro publish script
# Usage:
#   powershell -File scripts\publish.ps1                    # framework-dependent (butuh .NET Desktop Runtime 6)
#   powershell -File scripts\publish.ps1 -SelfContained     # self-contained (tidak butuh runtime)
param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Write-Host "=== Building Release ===" -ForegroundColor Cyan
dotnet build -c Release --nologo
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "build failed" }

$scFlag = "false"
if ($SelfContained) { $scFlag = "true" }

Write-Host "=== Publishing KasirPro (customer app) ===" -ForegroundColor Cyan
dotnet publish src/KasirPro.App -c Release -r win-x64 --self-contained $scFlag -o dist/KasirPro
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "publish app failed" }

Write-Host "=== Publishing KasirPro.Keygen (developer tools) ===" -ForegroundColor Cyan
dotnet publish tools/KasirPro.Keygen -c Release -r win-x64 --self-contained false -o dist/DeveloperTools/KasirPro.Keygen
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "publish keygen failed" }

Write-Host "=== Preparing runtime folders in dist/KasirPro ===" -ForegroundColor Cyan
foreach ($d in @("dist\KasirPro\Data", "dist\KasirPro\Backup", "dist\KasirPro\Images\Products",
                 "dist\KasirPro\Exports\Reports", "dist\KasirPro\Logs")) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

# safety: customer package must never contain key material
$danger = @("dist\KasirPro\license.dat", "dist\KasirPro\Keys")
foreach ($d in $danger) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force; Write-Host "  removed forbidden item: $d" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "Publish complete:" -ForegroundColor Green
Write-Host "  dist\KasirPro\KasirPro.exe            (customer app)"
Write-Host "  dist\DeveloperTools\KasirPro.Keygen\  (MASTER KEYGEN - jangan dibagikan)"
Pop-Location
