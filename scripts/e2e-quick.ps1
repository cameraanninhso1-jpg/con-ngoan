<#
  Chạy nhanh trong Windows Sandbox: cài bộ cài rồi chạy toàn bộ test đầu-cuối (không gồm test khóa phiên).
  Kết quả: C:\cmc\artifacts\e2e-quick\
#>
$ErrorActionPreference = 'Continue'
$out = 'C:\cmc\artifacts\e2e-quick'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$env:DOTNET_ROOT = 'C:\dotnet'; $env:PATH = "C:\dotnet;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
robocopy C:\cmc C:\work /E /XD artifacts .vs TestResults /NFL /NDL /NJH /NJS /NP | Out-Null
Start-Process C:\cmc\artifacts\installer\ConNgoan-Setup-x64.exe -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
$plain = 'Cmc-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + '!9'
New-LocalUser -Name cmcchild -Password (ConvertTo-SecureString $plain -AsPlainText -Force) -AccountNeverExpires | Out-Null
Add-LocalGroupMember -SID 'S-1-5-32-544' -Member cmcchild # giống sandbox-test.ps1: test chuyển Administrator → Standard User
$env:CMC_CHILD_PW = $plain
$env:CMC_E2E = '1'
dotnet test C:\work\tests\CaoMinhComputer.IntegrationTests\CaoMinhComputer.IntegrationTests.csproj -c Release --no-build `
    --filter 'FullyQualifiedName~EndToEndTests' --logger 'console;verbosity=detailed' 2>&1 | Out-File (Join-Path $out 'test.txt') -Encoding UTF8
Get-Content 'C:\ProgramData\ConNgoan\caominh.db' -ErrorAction SilentlyContinue | Out-Null
'DONE' | Set-Content (Join-Path $out 'DONE.txt')
