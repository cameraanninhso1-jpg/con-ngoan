<#
  Chạy trên máy build (KHÔNG chạy trong Sandbox): chuẩn bị dữ liệu cho test cập nhật trong Sandbox.
  - Build bộ cài thử nghiệm phiên bản 1.4.0.99 (giữ nguyên bộ cài 1.4.0 trong artifacts\installer).
  - Ký version.json bằng khóa bí mật thật (nằm ngoài thư mục mã nguồn) → artifacts\e2e-update\feed
  - Tạo thêm feed-badhash: manifest ký hợp lệ nhưng SHA-256 không khớp bộ cài (phải bị từ chối).
  Cách dùng: powershell -ExecutionPolicy Bypass -File scripts\prepare-update-e2e.ps1 -Key <đường dẫn .pem>
#>
param([Parameter(Mandatory)][string]$Key)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$out = Join-Path $root 'artifacts\e2e-update'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

$installer = 'artifacts\installer\ConNgoan-Setup-x64.exe'
$keep = Join-Path $env:TEMP 'cmc-installer-keep.exe'
Copy-Item $installer $keep -Force
try {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Version '1.4.0.99'
    $test = Join-Path $out 'ConNgoan-Setup-1.4.0.99.exe'
    Move-Item $installer $test -Force
} finally {
    Copy-Item $keep $installer -Force
    Remove-Item $keep -Force
}

$tool = Join-Path $PSScriptRoot 'cmc-release.ps1'
$url = 'file:///C:/cmc/artifacts/e2e-update/ConNgoan-Setup-1.4.0.99.exe'
& $tool sign -Key $Key -Installer $test -Version 1.4.0.99 -Url $url -Notes 'Bản thử nghiệm cập nhật tự động.' -Out (Join-Path $out 'feed')

# Cùng kích thước nhưng khác 1 byte → SHA-256 khác.
$bytes = [IO.File]::ReadAllBytes($test); $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
$fake = Join-Path $env:TEMP 'cmc-fake-installer.exe'; [IO.File]::WriteAllBytes($fake, $bytes)
& $tool sign -Key $Key -Installer $fake -Version 1.4.0.99 -Url $url -Out (Join-Path $out 'feed-badhash')
Remove-Item $fake -Force
& $tool verify -Out (Join-Path $out 'feed') -Installer $test
