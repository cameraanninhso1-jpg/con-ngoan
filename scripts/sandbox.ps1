<#
  Mở Windows Sandbox để kiểm thử, tự điền đường dẫn thư mục dự án vào file cấu hình.
  Cách dùng:
    powershell -ExecutionPolicy Bypass -File scripts\sandbox.ps1 -Name Test         # bộ test đầy đủ (sandbox-test.ps1)
    powershell -ExecutionPolicy Bypass -File scripts\sandbox.ps1 -Name E2E-Quick    # chỉ test đầu-cuối
    powershell -ExecutionPolicy Bypass -File scripts\sandbox.ps1 -Name E2E-Update   # test cập nhật (cần prepare-update-e2e.ps1)
  Yêu cầu: Windows Pro/Enterprise đã bật tính năng Windows Sandbox, đã build Release và chạy scripts\publish.ps1.
#>
param(
    [ValidateSet('Test', 'E2E-Quick', 'E2E-Update', 'AppLocker-Quick', 'AppLocker-Diag')]
    [string]$Name = 'Test'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$template = Get-Content (Join-Path $PSScriptRoot "sandbox\$Name.wsb.template") -Raw -Encoding UTF8
$out = Join-Path $repo "artifacts\$Name.wsb"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IO.File]::WriteAllText($out, $template.Replace('{{REPO}}', $repo), (New-Object Text.UTF8Encoding($false)))
Write-Host "Mở $out"
Invoke-Item $out
