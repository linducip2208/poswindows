# Build release solution
param()
$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
Write-Host "=== dotnet restore ===" -ForegroundColor Cyan
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "restore failed" }
Write-Host "=== dotnet build -c Release ===" -ForegroundColor Cyan
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "=== dotnet test -c Release ===" -ForegroundColor Cyan
dotnet test tests/KasirPro.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "tests failed" }
Write-Host "BUILD OK" -ForegroundColor Green
