<#
  Chạy BÊN TRONG Windows Sandbox (do scripts\CaoMinhComputer-Test.wsb gọi khi đăng nhập).
  - Chạy unit/integration test
  - Cài bộ cài thật, chạy test đầu-cuối (sửa HOSTS/registry thật của Sandbox)
  - Tự động thao tác giao diện (UI Automation) và chụp màn hình từng trang
  - Đo RAM/CPU của dịch vụ khi nhàn rỗi và của giao diện
  - Gỡ cài đặt và kiểm tra hệ thống được khôi phục
  Kết quả ghi về máy thật: artifacts\sandbox\
  Mật khẩu dưới đây chỉ là giá trị thử nghiệm dùng một lần trong Sandbox.
#>
$ErrorActionPreference = 'Continue'
$src = 'C:\cmc'
$work = 'C:\work'
$out = Join-Path $src 'artifacts\sandbox'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Start-Transcript -Path (Join-Path $out 'transcript.txt') | Out-Null

$env:DOTNET_ROOT = 'C:\dotnet'
$env:PATH = "C:\dotnet;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$summary = [System.Collections.Generic.List[string]]::new()
function Step([string]$name) { Write-Host "`n===== $name ($(Get-Date -Format HH:mm:ss)) =====" }
function Note([string]$text) { Write-Host $text; $summary.Add($text) }

Step 'Môi trường'
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Note "OS: $($os.ProductName) $($os.DisplayVersion) build $($os.CurrentBuild) ($($os.EditionID))"
Note "Smart App Control: $((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState)"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
Note "Tài khoản: $($identity.Name), elevated: $(([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
Note "CPU: $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name), lõi logic: $([Environment]::ProcessorCount)"

Step 'Sao chép mã nguồn đã build'
robocopy $src $work /E /XD artifacts .vs TestResults /NFL /NDL /NJH /NJS /NP | Out-Null

Step 'Unit + integration test'
dotnet test "$work\CaoMinhComputer.sln" -c Release --no-build --logger "trx;LogFileName=unit.trx" --results-directory $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'unit-tests.txt') | Select-Object -Last 8
Note "Unit/integration test exit code: $LASTEXITCODE"

Step 'Cài đặt bằng installer'
$setup = Join-Path $src 'artifacts\installer\ConNgoan-Setup-x64.exe'
$p = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$out\setup.log`"" -Wait -PassThru
Note "Installer exit code: $($p.ExitCode)"
Start-Sleep 3
$svc = Get-Service ConNgoanService -ErrorAction SilentlyContinue
Note "Dịch vụ sau khi cài: $($svc.Status) / StartType $($svc.StartType)"
icacls 'C:\ProgramData\ConNgoan' | Out-File (Join-Path $out 'acl.txt')
Get-Content (Join-Path $out 'acl.txt') | Write-Host

Step 'Test đầu-cuối (HOSTS, dịch vụ, ACL, chính sách, tài khoản)'
# Tài khoản "con" thử nghiệm (chỉ tồn tại trong Sandbox, bị xóa khi đóng Sandbox).
$childPlain = 'Cmc-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + '!9'
$childPw = ConvertTo-SecureString $childPlain -AsPlainText -Force
$env:CMC_CHILD_PW = $childPlain # chỉ dùng trong Sandbox để test chạy chương trình bằng tài khoản con
try {
    New-LocalUser -Name 'cmcchild' -Password $childPw -AccountNeverExpires -ErrorAction Stop | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'cmcchild' -ErrorAction Stop
    Note 'Đã tạo tài khoản thử nghiệm cmcchild (Administrator, chưa đăng nhập).'
} catch {
    Note "Không tạo được tài khoản thử nghiệm: $($_.Exception.Message)"
}
$env:CMC_E2E = '1'
dotnet test "$work\tests\CaoMinhComputer.IntegrationTests\CaoMinhComputer.IntegrationTests.csproj" -c Release --no-build --filter 'FullyQualifiedName~EndToEndTests' --logger "trx;LogFileName=e2e.trx" --results-directory $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'e2e-tests.txt') | Select-Object -Last 8
Note "E2E test exit code: $LASTEXITCODE"
Remove-Item Env:CMC_E2E

Step 'Cài đè (nâng cấp) bằng chính bộ cài'
$logsBefore = (Get-ChildItem 'C:\ProgramData\ConNgoan\caominh.db').Length
$p = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$out\setup-upgrade.log`"" -Wait -PassThru
Start-Sleep 3
$svc = Get-Service ConNgoanService -ErrorAction SilentlyContinue
Note "Cài đè: exit code $($p.ExitCode), dịch vụ $($svc.Status), database còn nguyên: $((Test-Path 'C:\ProgramData\ConNgoan\caominh.db') -and ((Get-ChildItem 'C:\ProgramData\ConNgoan\caominh.db').Length -ge $logsBefore))"
Note "Agent được đăng ký khởi động cùng Windows: $([bool](Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ConNgoanAgent -ErrorAction SilentlyContinue))"

Step 'Đo tài nguyên dịch vụ khi nhàn rỗi (120 giây)'
$proc = Get-Process 'ConNgoan.Service' -ErrorAction SilentlyContinue
if ($proc) {
    Start-Sleep 20
    $proc.Refresh()
    $cpu0 = $proc.TotalProcessorTime
    $samples = @()
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep 10; $proc.Refresh(); $samples += $proc.WorkingSet64 }
    $cpu = ($proc.TotalProcessorTime - $cpu0).TotalMilliseconds / (120000 * [Environment]::ProcessorCount) * 100
    Note ("Service idle: Working set TB {0:N1} MB (max {1:N1} MB), Private {2:N1} MB, CPU trung bình {3:N3}% trong 120 s" -f `
        (($samples | Measure-Object -Average).Average / 1MB), (($samples | Measure-Object -Maximum).Maximum / 1MB), ($proc.PrivateMemorySize64 / 1MB), $cpu)
} else {
    Note 'Không tìm thấy tiến trình dịch vụ để đo.'
}

Step 'Kiểm thử giao diện bằng UI Automation'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Find-ById($root, [string]$id, [int]$timeoutSec = 15) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $el = $root.FindFirst($TS::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}
function Set-Value($el, [string]$value) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value) }
function Invoke-El($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Select-El($el) { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Shot($window, [string]$name) {
    Start-Sleep -Milliseconds 1500
    $r = $window.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $out "ui-$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$uiExe = 'C:\Program Files\ConNgoan\ConNgoan.exe'
$ui = Start-Process $uiExe -PassThru
$window = $null
for ($i = 0; $i -lt 40 -and -not $window; $i++) {
    Start-Sleep -Milliseconds 500
    $ui.Refresh()
    if ($ui.MainWindowHandle -ne 0) { $window = $AE::FromHandle($ui.MainWindowHandle) }
}
if ($window) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $ui.Refresh()
    Note ("UI sau khi mở: Working set {0:N1} MB" -f ($ui.WorkingSet64 / 1MB))
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Maximized')
    Shot $window '01-setup'

    $user = Find-ById $window 'UsernameBox'
    if ($user) {
        Set-Value $user 'phuhuynh'
        Set-Value (Find-ById $window 'PasswordBox') 'SandboxTest2024'
        Set-Value (Find-ById $window 'ConfirmBox') 'SandboxTest2024'
        Invoke-El (Find-ById $window 'CreateButton')
    }
    $navWeb = Find-ById $window 'NavWeb' 20
    if ($navWeb) {
        Note 'UI: tạo tài khoản quản trị và vào màn hình chính thành công.'
        Shot $window '02-web'
        foreach ($nav in 'NavInstall', 'NavBreak', 'NavTime', 'NavAdmin', 'NavSecurity', 'NavLogs', 'NavAbout') {
            $el = Find-ById $window $nav
            if ($el) { Select-El $el; Shot $window ("03-" + $nav.Substring(3).ToLower()) }
        }
        # Đăng xuất và chụp màn hình đăng nhập
        Select-El (Find-ById $window 'NavWeb')
        $ui.Refresh()
        Note ("UI sau khi duyệt các trang: Working set {0:N1} MB" -f ($ui.WorkingSet64 / 1MB))
    } else {
        Note 'UI: KHÔNG vào được màn hình chính sau khi tạo tài khoản.'
        Shot $window '02-error'
    }
    Stop-Process -Id $ui.Id -Force
    Start-Sleep 2
    $ui2 = Start-Process $uiExe -PassThru
    for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; $ui2.Refresh(); if ($ui2.MainWindowHandle -ne 0) { break } }
    if ($ui2.MainWindowHandle -ne 0) {
        $w2 = $AE::FromHandle($ui2.MainWindowHandle)
        $w2.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Maximized')
        Shot $w2 '00-login'
    }
    Stop-Process -Id $ui2.Id -Force -ErrorAction SilentlyContinue
} else {
    Note 'UI: cửa sổ không xuất hiện trong 20 giây.'
}

Step 'Cưỡng chế hết giờ (sẽ cảnh báo rồi KHÓA phiên Sandbox)'
$env:CMC_E2E_LOCK = '1'
$lock = Start-Process 'C:\dotnet\dotnet.exe' -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $out 'lock-tests.txt') -ArgumentList @(
    'test', "$work\tests\CaoMinhComputer.IntegrationTests\CaoMinhComputer.IntegrationTests.csproj", '-c', 'Release', '--no-build',
    '--filter', 'FullyQualifiedName~TimeEnforcementEndToEndTests', '--logger', 'trx;LogFileName=lock.trx', '--results-directory', $out)
$null = $lock.Handle # giữ handle để đọc được ExitCode
Start-Sleep 25
try {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $out 'ui-04-time-warning.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
} catch { Write-Host "Không chụp được màn hình cảnh báo: $($_.Exception.Message)" }
$null = $lock.WaitForExit(300000)
Note "Lock test exit code: $($lock.ExitCode)"
Remove-Item Env:CMC_E2E_LOCK

Step 'Gỡ cài đặt'
$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$p = Start-Process 'C:\Program Files\ConNgoan\unins000.exe' -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$out\uninstall.log`"" -Wait -PassThru
Start-Sleep 3
Note "Uninstaller exit code: $($p.ExitCode)"
Note "Dịch vụ sau khi gỡ: $(if (Get-Service ConNgoanService -ErrorAction SilentlyContinue) { 'VẪN CÒN' } else { 'đã xóa' })"
Note "HOSTS còn khối ConNgoan: $([bool](Select-String -Path $hostsPath -Pattern 'ConNgoan BEGIN' -Quiet))"
Note "Thư mục Program Files còn tồn tại: $(Test-Path 'C:\Program Files\ConNgoan')"
Note "Khóa Run của Agent còn lại: $([bool](Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ConNgoanAgent -ErrorAction SilentlyContinue))"
try {
    New-Item -ItemType Directory -Force C:\CmcTest | Out-Null
    Copy-Item "$env:SystemRoot\System32\whoami.exe" C:\CmcTest\after-uninstall.exe -Force
    $cred = New-Object PSCredential("$env:COMPUTERNAME\cmcchild", $childPw)
    $p = Start-Process C:\CmcTest\after-uninstall.exe -ArgumentList '/user' -Credential $cred -WorkingDirectory C:\Windows\System32 -WindowStyle Hidden -PassThru -Wait -ErrorAction Stop
    Note "Sau khi gỡ, con chạy được file portable (AppLocker đã gỡ sạch): exit $($p.ExitCode)"
} catch { Note "Sau khi gỡ, con VẪN bị chặn hoặc lỗi: $($_.Exception.Message)" }

$summary | Set-Content (Join-Path $out 'summary.txt') -Encoding UTF8
Stop-Transcript | Out-Null
'DONE' | Set-Content (Join-Path $out 'DONE.txt')
