<#
  Build toàn bộ solution và chạy unit/integration test.
  Cách dùng:  powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Configuration Release]
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> dotnet build ($Configuration)" -ForegroundColor Cyan
dotnet build CaoMinhComputer.sln -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build thất bại." }

if (-not $SkipTests) {
    Write-Host "==> dotnet test" -ForegroundColor Cyan
    dotnet test CaoMinhComputer.sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Có test thất bại." }
}
Write-Host "Build thành công." -ForegroundColor Green
