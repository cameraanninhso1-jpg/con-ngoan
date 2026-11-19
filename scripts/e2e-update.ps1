<#
  Chạy trong Windows Sandbox (scripts\E2E-Update.wsb): unit test + test cập nhật thật 1.4.0 → 1.4.0.99.
  Cần chạy scripts\prepare-update-e2e.ps1 trên máy build trước. Kết quả: C:\cmc\artifacts\e2e-update-result\
#>
$ErrorActionPreference = 'Continue'
$out = 'C:\cmc\artifacts\e2e-update-result'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$env:DOTNET_ROOT = 'C:\dotnet'; $env:PATH = "C:\dotnet;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
robocopy C:\cmc C:\work /E /XD artifacts .vs TestResults /NFL /NDL /NJH /NJS /NP | Out-Null
dotnet test C:\work\tests\CaoMinhComputer.Core.Tests\CaoMinhComputer.Core.Tests.csproj -c Release --no-build 2>&1 |
    Out-File (Join-Path $out 'unit.txt') -Encoding UTF8
Start-Process C:\cmc\artifacts\installer\ConNgoan-Setup-x64.exe -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
Start-Sleep 5
$env:CMC_E2E = '1'; $env:CMC_E2E_UPDATE = '1'
dotnet test C:\work\tests\CaoMinhComputer.IntegrationTests\CaoMinhComputer.IntegrationTests.csproj -c Release --no-build `
    --filter 'FullyQualifiedName~UpdateFlowTests' --logger 'console;verbosity=detailed' 2>&1 | Out-File (Join-Path $out 'test.txt') -Encoding UTF8
Copy-Item 'C:\ProgramData\ConNgoan\updates\setup.log' (Join-Path $out 'update-setup.log') -ErrorAction SilentlyContinue
(Get-Item 'C:\Program Files\ConNgoan\ConNgoan.Service.exe').VersionInfo.FileVersion | Set-Content (Join-Path $out 'installed-version.txt')
'DONE' | Set-Content (Join-Path $out 'DONE.txt')
