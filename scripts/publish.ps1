<#
  Publish giao diện + dịch vụ cho win-x64 vào artifacts\publish, sau đó tạo bộ cài bằng Inno Setup.
  Cách dùng:
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1                 # self-contained (khuyên dùng)
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -FrameworkDependent   # cần .NET 10 Desktop Runtime trên máy đích
#>
param(
    [switch]$FrameworkDependent,
    [switch]$SkipInstaller,
    [string]$Version = '1.6.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root 'artifacts\publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

$common = @('-c', 'Release', '-r', 'win-x64', "--self-contained=$selfContained", '-o', $out,
            "-p:Version=$Version", '-p:DebugType=none', '-p:GenerateDocumentationFile=false')

Write-Host "==> Publish UI (self-contained=$selfContained)" -ForegroundColor Cyan
dotnet publish src\CaoMinhComputer.UI\CaoMinhComputer.UI.csproj @common
if ($LASTEXITCODE -ne 0) { throw 'Publish UI thất bại.' }

Write-Host "==> Publish Service" -ForegroundColor Cyan
dotnet publish src\CaoMinhComputer.Service\CaoMinhComputer.Service.csproj @common
if ($LASTEXITCODE -ne 0) { throw 'Publish Service thất bại.' }

Write-Host "==> Publish Agent" -ForegroundColor Cyan
dotnet publish src\CaoMinhComputer.Agent\CaoMinhComputer.Agent.csproj @common
if ($LASTEXITCODE -ne 0) { throw 'Publish Agent thất bại.' }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Dung lượng thư mục publish: {0:N1} MB" -f $size) -ForegroundColor Green

if ($SkipInstaller) { return }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning 'Không tìm thấy Inno Setup 6 (ISCC.exe). Cài từ https://jrsoftware.org/isdl.php rồi chạy lại.'
    return
}

Write-Host "==> Inno Setup" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$out" installer\CaoMinhComputer.iss
if ($LASTEXITCODE -ne 0) { throw 'Tạo installer thất bại.' }
Get-Item artifacts\installer\ConNgoan-Setup-x64.exe | ForEach-Object {
    Write-Host ("Installer: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green
}
