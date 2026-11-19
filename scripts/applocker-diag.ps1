<#
  Thí nghiệm AppLocker trong Windows Sandbox (lần 5):
  1) Kiểm tra luồng Group Policy (gpsvc, gpupdate).
  2) Áp dụng quy tắc qua MDM Bridge (AppLocker CSP) bằng quyền SYSTEM – cách Intune dùng, không cần Group Policy.
  Kết quả: C:\cmc\artifacts\applocker-diag\result.txt
#>
$ErrorActionPreference = 'Continue'
$out = 'C:\cmc\artifacts\applocker-diag'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $out 'result.txt'
function W($t) { $t | Tee-Object -FilePath $log -Append | Write-Host }

$pw = 'Cmc-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + '!9'
New-LocalUser -Name diagchild -Password (ConvertTo-SecureString $pw -AsPlainText -Force) -AccountNeverExpires | Out-Null
$cred = New-Object PSCredential("$env:COMPUTERNAME\diagchild", (ConvertTo-SecureString $pw -AsPlainText -Force))
$sid = (New-Object Security.Principal.NTAccount("$env:COMPUTERNAME\diagchild")).Translate([Security.Principal.SecurityIdentifier]).Value
W "Child SID $sid"
New-Item -ItemType Directory -Force C:\DiagTest | Out-Null
$whoami = "$env:SystemRoot\System32\whoami.exe"
function RunChild([string]$exe) {
    try {
        $p = Start-Process $exe -ArgumentList '/user' -Credential $cred -WorkingDirectory C:\Windows\System32 -WindowStyle Hidden -PassThru -Wait -ErrorAction Stop
        return "exit $($p.ExitCode)"
    } catch { return "BLOCKED/ERR: $($_.Exception.Message)" }
}
W "Tạo hồ sơ: $(RunChild $whoami)"

# 1) Group Policy
$gp = Get-Service gpsvc -ErrorAction SilentlyContinue
W "gpsvc: $($gp.Status)/$($gp.StartType)"
$gpu = & "$env:SystemRoot\System32\gpupdate.exe" /target:computer /force 2>&1 | Out-String
W "gpupdate: $($gpu.Trim() -replace '\s+', ' ')"

# 2) MDM Bridge bằng SYSTEM (qua Scheduled Task)
Copy-Item $whoami C:\DiagTest\mdm-before.exe -Force
W "Trước MDM, mdm-before.exe: $(RunChild 'C:\DiagTest\mdm-before.exe')"
$rule = @"
<RuleCollection Type="Exe" EnforcementMode="Enabled">
  <FilePathRule Id="a61c8b2c-a319-4cd0-9690-d2177cad7b51" Name="allow all" Description="" UserOrGroupSid="S-1-1-0" Action="Allow">
    <Conditions><FilePathCondition Path="*" /></Conditions>
  </FilePathRule>
  <FilePathRule Id="b61c8b2c-a319-4cd0-9690-d2177cad7b52" Name="deny child" Description="" UserOrGroupSid="$sid" Action="Deny">
    <Conditions><FilePathCondition Path="*" /></Conditions>
    <Exceptions><FilePathCondition Path="%WINDIR%\*" /><FilePathCondition Path="%PROGRAMFILES%\*" /></Exceptions>
  </FilePathRule>
</RuleCollection>
"@
$rule | Set-Content (Join-Path $out 'mdm-rule.xml') -Encoding UTF8
$sysScript = @'
$ErrorActionPreference = 'Stop'
$out = 'C:\cmc\artifacts\applocker-diag'
try {
  $policy = [System.Security.SecurityElement]::Escape((Get-Content (Join-Path $out 'mdm-rule.xml') -Raw))
  $ns = 'root\cimv2\mdm\dmmap'
  $cls = 'MDM_AppLocker_ApplicationLaunchRestrictions01_EXE03'
  Get-CimInstance -Namespace $ns -ClassName $cls -ErrorAction SilentlyContinue | Where-Object { $_.ParentID -like '*CaoMinhDiag*' } | Remove-CimInstance
  New-CimInstance -Namespace $ns -ClassName $cls -Property @{
    ParentID = './Vendor/MSFT/AppLocker/ApplicationLaunchRestrictions/CaoMinhDiag'
    InstanceID = 'EXE'
    Policy = $policy
  } | Out-Null
  "MDM OK $(whoami)" | Set-Content (Join-Path $out 'mdm-system.txt')
} catch { "MDM LỖI $(whoami): $($_.Exception.Message)" | Set-Content (Join-Path $out 'mdm-system.txt') }
'@
$sysScript | Set-Content (Join-Path $out 'mdm-system.ps1') -Encoding UTF8
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$out\mdm-system.ps1`""
Register-ScheduledTask -TaskName CmcDiagMdm -Action $action -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Start-ScheduledTask -TaskName CmcDiagMdm
for ($i = 0; $i -lt 60 -and -not (Test-Path "$out\mdm-system.txt"); $i++) { Start-Sleep 1 }
W "Kết quả SYSTEM: $(Get-Content "$out\mdm-system.txt" -ErrorAction SilentlyContinue)"
Start-Sleep 5
W "Thư mục AppLocker: $((Get-ChildItem "$env:SystemRoot\System32\AppLocker" -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Replace("$env:SystemRoot\System32\AppLocker\", '') }) -join ', ')"

Copy-Item $whoami C:\DiagTest\mdm-fresh.exe -Force
W "Sau MDM, file mới mdm-fresh.exe: $(RunChild 'C:\DiagTest\mdm-fresh.exe')"
W "Sau MDM, file đã chạy mdm-before.exe: $(RunChild 'C:\DiagTest\mdm-before.exe')"
W "Sau MDM, whoami System32: $(RunChild $whoami)"
try {
    $p = Start-Process 'C:\DiagTest\mdm-fresh.exe' -ArgumentList '/user' -WindowStyle Hidden -PassThru -Wait
    W "Sau MDM, admin chạy mdm-fresh.exe: exit $($p.ExitCode)"
} catch { W "Sau MDM, admin chạy mdm-fresh.exe: LỖI $($_.Exception.Message)" }
$ev = Get-WinEvent -LogName 'Microsoft-Windows-AppLocker/EXE and DLL' -MaxEvents 3 -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id): $($_.Message.Split("`n")[0])" }
W "Sự kiện AppLocker: $($ev -join ' | ')"
'DONE' | Set-Content (Join-Path $out 'DONE.txt')
