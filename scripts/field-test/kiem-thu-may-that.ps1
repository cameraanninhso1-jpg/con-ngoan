<#
  Bộ kiểm thử Con Ngoan trên MÁY THẬT (Windows 10/11).
  Cách chạy: nhấp chuột phải "CHAY-KIEM-THU.bat" → Run as administrator.
  Script KHÔNG cần mật khẩu, không gửi dữ liệu ra ngoài. Kết quả ghi vào thư mục "ket-qua-..." cạnh script.

  Các bước:
   1. Thu thập thông tin máy (Windows, edition, RAM/CPU, UAC, Smart App Control, phần mềm diệt virus).
   2. Sao lưu file HOSTS hiện tại.
   3. Cài ConNgoan-Setup-x64.exe ở chế độ im lặng (hoặc cài đè nếu đã có).
   4. Kiểm tra dịch vụ, quyền thư mục dữ liệu, khóa khởi động Agent, Named Pipe, AppLocker CSP.
   5. Đo RAM/CPU của dịch vụ khi nhàn rỗi (120 giây) và của giao diện khi mở; chụp màn hình.
  Tham số: -GoCaiDat  → gỡ cài đặt và kiểm tra hệ thống được khôi phục (chạy sau khi kiểm thử thủ công xong).
#>
param([switch]$GoCaiDat)

$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Cần chạy bằng quyền Administrator (chuột phải CHAY-KIEM-THU.bat → Run as administrator).' -ForegroundColor Red
    Read-Host 'Nhấn Enter để thoát'
    exit 1
}

$out = Join-Path $here ("ket-qua-$env:COMPUTERNAME-" + (Get-Date -Format 'yyyyMMdd-HHmm') + ($(if ($GoCaiDat) { '-go-cai-dat' } else { '' })))
New-Item -ItemType Directory -Force $out | Out-Null
Start-Transcript -Path (Join-Path $out 'transcript.txt') | Out-Null
$summary = [System.Collections.Generic.List[string]]::new()
function Step([string]$name) { Write-Host "`n===== $name =====" -ForegroundColor Cyan }
function Note([string]$text, [string]$level = 'OK') {
    $color = @{ OK = 'Green'; WARN = 'Yellow'; FAIL = 'Red'; INFO = 'Gray' }[$level]
    Write-Host "[$level] $text" -ForegroundColor $color
    $summary.Add("[$level] $text")
}
$installDir = 'C:\Program Files\ConNgoan'
$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"

Step 'Thông tin máy'
$os = Get-CimInstance Win32_OperatingSystem
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$build = [int]$cv.CurrentBuild
Note "Windows: $($os.Caption) | EditionID $($cv.EditionID) | phiên bản $($cv.DisplayVersion) | build $build.$($cv.UBR) | $(if ($build -ge 22000) {'Windows 11'} else {'Windows 10'})" INFO
Note "CPU: $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name) | RAM: $([math]::Round($os.TotalVisibleMemorySize/1MB,1)) GB" INFO
$lua = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue).EnableLUA
Note "UAC: $(if ($lua -eq 0) {'TẮT'} else {'bật'})" $(if ($lua -eq 0) {'WARN'} else {'INFO'})
$sac = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
Note "Smart App Control: $(switch ($sac) { 1 {'BẬT – bản chưa ký số sẽ bị chặn'} 2 {'đánh giá'} default {'không có/tắt'} })" $(if ($sac -eq 1) {'WARN'} else {'INFO'})
$av = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | ForEach-Object displayName
Note "Phần mềm diệt virus: $($av -join ', ')" INFO
$appLockerEdition = $cv.EditionID -match '^(Professional|Enterprise|Education)'
Note "AppLocker theo edition: $(if ($appLockerEdition) {'có (Pro/Enterprise/Education)'} else {'KHÔNG (Home) – sẽ dùng mức chặn cơ bản'})" INFO
try { $adminSids = @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop | ForEach-Object { $_.SID.Value }) } catch { $adminSids = @() }
$users = Get-LocalUser | Where-Object Enabled | ForEach-Object {
    "$($_.Name) ($(if ($adminSids -contains $_.SID.Value) {'Administrator'} else {'Standard User'}))"
}
Note "Tài khoản đang bật: $($users -join ', ')" INFO

if ($GoCaiDat) {
    Step 'Gỡ cài đặt'
    $unins = Get-ChildItem $installDir -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $unins) { Note 'Không tìm thấy bộ gỡ cài đặt.' FAIL }
    else {
        $p = Start-Process $unins.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$out\uninstall.log`"" -Wait -PassThru
        Start-Sleep 5
        Note "Bộ gỡ cài đặt: exit code $($p.ExitCode)" $(if ($p.ExitCode -eq 0) {'OK'} else {'FAIL'})
        Note "Dịch vụ: $(if (Get-Service ConNgoanService -ErrorAction SilentlyContinue) {'VẪN CÒN'} else {'đã xóa'})" $(if (Get-Service ConNgoanService -ErrorAction SilentlyContinue) {'FAIL'} else {'OK'})
        $left = Select-String -Path $hostsPath -Pattern 'ConNgoan BEGIN' -Quiet
        Note "HOSTS còn khối ConNgoan: $left" $(if ($left) {'FAIL'} else {'OK'})
        $run = Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ConNgoanAgent -ErrorAction SilentlyContinue
        Note "Khóa khởi động Agent còn lại: $([bool]$run)" $(if ($run) {'FAIL'} else {'OK'})
        Note "Thư mục cài đặt còn tồn tại: $(Test-Path $installDir)" $(if (Test-Path $installDir) {'WARN'} else {'OK'})
        Note 'Hãy đăng nhập tài khoản của con và mở thử một game/ứng dụng từng bị chặn – phải chạy được bình thường.' INFO
    }
    $summary | Set-Content (Join-Path $out 'tom-tat.txt') -Encoding UTF8
    Stop-Transcript | Out-Null
    Write-Host "`nXong. Kết quả: $out" -ForegroundColor Green
    Read-Host 'Nhấn Enter để đóng'
    exit 0
}

Step 'Sao lưu HOSTS'
Copy-Item $hostsPath (Join-Path $out 'hosts-truoc-khi-cai.txt') -ErrorAction SilentlyContinue
Note 'Đã sao lưu HOSTS hiện tại vào thư mục kết quả.' INFO

Step 'Cài đặt'
$setup = Join-Path $here 'ConNgoan-Setup-x64.exe'
$had = Test-Path (Join-Path $installDir 'ConNgoan.exe')
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$out\setup.log`"" -Wait -PassThru
Note "$(if ($had) {'Cài đè (nâng cấp)'} else {'Cài mới'}): exit code $($p.ExitCode), $([int]$sw.Elapsed.TotalSeconds) giây" $(if ($p.ExitCode -eq 0) {'OK'} else {'FAIL'})
Start-Sleep 5
$ver = (Get-Item (Join-Path $installDir 'ConNgoan.exe') -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
Note "Phiên bản đã cài: $ver" INFO

Step 'Kiểm tra sau khi cài'
$svc = Get-Service ConNgoanService -ErrorAction SilentlyContinue
Note "Dịch vụ: $($svc.Status) / $($svc.StartType)" $(if ($svc.Status -eq 'Running') {'OK'} else {'FAIL'})
$old = Get-Service CaoMinhComputerService -ErrorAction SilentlyContinue
Note "Bản cũ CaoMinhComputer (tên trước khi đổi): $(if ($old) {"VẪN CÒN ($($old.Status)) – gỡ trong Settings › Apps để 2 bản không chạy song song"} else {'không có'})" $(if ($old) {'FAIL'} else {'OK'})
$acl = (Get-Acl 'C:\ProgramData\ConNgoan').Access | ForEach-Object { $_.IdentityReference.Value }
$aclOk = -not ($acl | Where-Object { $_ -match 'Users|Everyone|Authenticated' })
Note "Quyền thư mục dữ liệu: $($acl -join ', ')" $(if ($aclOk) {'OK'} else {'FAIL'})
$run = Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ConNgoanAgent -ErrorAction SilentlyContinue
Note "Agent khởi động cùng Windows: $([bool]$run)" $(if ($run) {'OK'} else {'FAIL'})
# Agent ở tài khoản quản trị: dịch vụ trả lời "không phải tài khoản con" → mã 2 nghĩa là pipe + xác thực tiến trình hoạt động.
$agent = Start-Process (Join-Path $installDir 'ConNgoan.Agent.exe') -ArgumentList '--report-once' -Wait -PassThru
Note "Named Pipe Agent ↔ dịch vụ: $(switch ($agent.ExitCode) { 2 {'hoạt động (tài khoản này không phải của con)'} 0 {'hoạt động (tài khoản này đang được đánh dấu là của con)'} 3 {'KHÔNG kết nối được dịch vụ'} default {"mã $($agent.ExitCode)"} })" $(if ($agent.ExitCode -in 0, 2) {'OK'} else {'FAIL'})
$appid = Get-Service AppIDSvc -ErrorAction SilentlyContinue
Note "Dịch vụ Application Identity (AppLocker): $($appid.Status) / $($appid.StartType)" INFO
$mdm = Get-CimClass -Namespace root\cimv2\mdm\dmmap -ClassName MDM_AppLocker_ApplicationLaunchRestrictions01_EXE03 -ErrorAction SilentlyContinue
Note "AppLocker CSP (MDM Bridge) trên máy này: $(if ($mdm) {'có'} else {'không có'})" $(if ($mdm -or -not $appLockerEdition) {'INFO'} else {'WARN'})

Step 'Đo tài nguyên dịch vụ khi nhàn rỗi (120 giây)'
$proc = Get-Process 'ConNgoan.Service' -ErrorAction SilentlyContinue
if ($proc) {
    Start-Sleep 20
    $proc.Refresh(); $cpu0 = $proc.TotalProcessorTime; $samples = @()
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep 10; $proc.Refresh(); $samples += $proc.WorkingSet64 }
    $cpu = ($proc.TotalProcessorTime - $cpu0).TotalMilliseconds / (120000 * [Environment]::ProcessorCount) * 100
    Note ("Dịch vụ nhàn rỗi: Working set TB {0:N1} MB, Private {1:N1} MB, CPU TB {2:N3}%" -f (($samples | Measure-Object -Average).Average / 1MB), ($proc.PrivateMemorySize64 / 1MB), $cpu) INFO
} else { Note 'Không thấy tiến trình dịch vụ để đo.' FAIL }

Step 'Mở giao diện, đo RAM, chụp màn hình'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$ui = Start-Process (Join-Path $installDir 'ConNgoan.exe') -PassThru
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; $ui.Refresh(); if ($ui.MainWindowHandle -ne 0) { break } }
if ($ui.MainWindowHandle -ne 0) {
    Start-Sleep 4; $ui.Refresh()
    Note ("Giao diện mở được, Working set {0:N1} MB" -f ($ui.WorkingSet64 / 1MB)) OK
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height); $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size); $bmp.Save((Join-Path $out 'man-hinh-giao-dien.png')); $g.Dispose(); $bmp.Dispose()
    Note 'Đã chụp màn hình giao diện (man-hinh-giao-dien.png). Cửa sổ app được để mở cho bạn thao tác tiếp.' INFO
} else { Note 'Cửa sổ giao diện không xuất hiện trong 20 giây.' FAIL }

$summary | Set-Content (Join-Path $out 'tom-tat.txt') -Encoding UTF8
Stop-Transcript | Out-Null
$fail = @($summary | Where-Object { $_ -like '[FAIL]*' }).Count
Write-Host "`nXong phần tự động: $fail lỗi. Kết quả nằm ở: $out" -ForegroundColor $(if ($fail) {'Red'} else {'Green'})
Write-Host 'Tiếp theo: làm theo HUONG-DAN-KIEM-THU.md (phần kiểm thử thủ công với tài khoản của con).' -ForegroundColor Yellow
Read-Host 'Nhấn Enter để đóng'
