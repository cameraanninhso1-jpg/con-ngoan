; =====================================================================
;  Con Ngoan (phát triển bởi Cao Minh Computer) - Inno Setup script
;  Build: scripts\publish.ps1  (hoặc ISCC.exe installer\CaoMinhComputer.iss)
;  Yêu cầu: đã publish vào artifacts\publish trước khi biên dịch.
; =====================================================================

#define MyAppName "Con Ngoan"
#define MyAppDirName "ConNgoan"
#define MyAppTagline "phát triển bởi Cao Minh Computer"
#ifndef MyAppVersion
  #define MyAppVersion "1.6.0"
#endif
#define MyAppPublisher "CÔNG TY TNHH CAO MINH THĂNG LONG"
#define MyAppURL "https://caominhcomputer.com/"
#define MyAppHotline "0936.203.209"
#define MyAppExeName "ConNgoan.exe"
#define MyServiceExe "ConNgoan.Service.exe"
#define MyServiceName "ConNgoanService"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{0EE103F4-819E-4C64-BC18-9FEFD602830F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} – {#MyAppTagline}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppSupportPhone={#MyAppHotline}
AppContact=Hotline {#MyAppHotline}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup – {#MyAppTagline}
DefaultDirName={autopf}\{#MyAppDirName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=ConNgoan-Setup-x64
SetupIconFile=..\src\CaoMinhComputer.UI\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
; Ký số khi phát hành thực tế (xem docs/INSTALL.md):
; SignTool=signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a $f

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=Con Ngoan – phần mềm giúp phụ huynh quản lý máy tính của con, phát triển và tài trợ miễn phí 100%% bởi CÔNG TY TNHH CAO MINH THĂNG LONG (Cao Minh Computer), chung tay cùng các bậc phụ huynh xây dựng môi trường máy tính an toàn, lành mạnh cho con em học tập.%n%nChương trình sẽ cài đặt [name] lên máy tính của bạn.

[CustomMessages]
english.TaskService=Tự khởi động dịch vụ bảo vệ cùng Windows (khuyên dùng)
english.TaskDesktop=Tạo biểu tượng ngoài Desktop
english.GroupTasks=Tùy chọn:
english.LaunchApp=Mở Con Ngoan
english.AskDeleteData=Xóa luôn dữ liệu của Con Ngoan (cấu hình, nhật ký, bản sao lưu HOSTS)?%n%nChọn "No" để giữ lại dữ liệu cho lần cài sau.
english.UnsupportedEdition=Con Ngoan yêu cầu Windows 10/11 64-bit.

[Tasks]
Name: "autostartservice"; Description: "{cm:TaskService}"; GroupDescription: "{cm:GroupTasks}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:GroupTasks}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Hướng dẫn cài đặt"; Filename: "{app}\docs\INSTALL.md"
Name: "{group}\Website Cao Minh Computer"; Filename: "{#MyAppURL}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Agent khởi động cùng Windows cho mọi tài khoản; tự thoát nếu không phải tài khoản của con.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ConNgoanAgent"; ValueData: """{app}\ConNgoan.Agent.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent shellexec

[Code]
function Sc(const Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log('sc.exe ' + Params + ' => ' + IntToStr(Code));
  Result := Code;
end;

procedure StopService();
var
  Code: Integer;
begin
  Sc('stop {#MyServiceName}');
  { Đóng Agent đang chạy trong các phiên để thay/gỡ file. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ConNgoan.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(2000);
end;

function InitializeSetup(): Boolean;
begin
  Result := IsWin64 and (GetWindowsVersion >= $0A000000);
  if not Result then
    MsgBox(CustomMessage('UnsupportedEdition'), mbCriticalError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Nâng cấp: dừng dịch vụ cũ để thay file. }
  StopService();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  BinPath, StartType: String;
begin
  if CurStep = ssPostInstall then
  begin
    { 1. Tạo thư mục dữ liệu (ACL chỉ SYSTEM + Administrators) và database. }
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--prepare-data', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Log('prepare-data => ' + IntToStr(Code));

    { 2. Cài / cập nhật Windows Service (đường dẫn có dấu nháy để tránh lỗ hổng unquoted path). }
    BinPath := '"\"' + ExpandConstant('{app}\{#MyServiceExe}') + '\""';
    if WizardIsTaskSelected('autostartservice') then
      StartType := 'auto'
    else
      StartType := 'demand';

    if Sc('create {#MyServiceName} binPath= ' + BinPath + ' start= ' + StartType + ' DisplayName= "Con Ngoan - Dich vu bao ve"') <> 0 then
      Sc('config {#MyServiceName} binPath= ' + BinPath + ' start= ' + StartType);
    Sc('description {#MyServiceName} "Dịch vụ bảo vệ của Con Ngoan (Cao Minh Computer): tự khôi phục cấu hình chặn website và ứng dụng."');
    Sc('failure {#MyServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000');
    Sc('start {#MyServiceName}');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Khôi phục HOSTS, DisallowRun, AppLocker, chính sách Windows do Con Ngoan tạo.
      Chạy TRƯỚC khi dừng dịch vụ: AppLocker CSP chỉ gỡ được bằng quyền SYSTEM (dịch vụ làm qua Named Pipe). }
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--uninstall-cleanup', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Log('uninstall-cleanup => ' + IntToStr(Code));
    StopService();
    Sc('delete {#MyServiceName}');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if (not UninstallSilent) and
       (MsgBox(CustomMessage('AskDeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{commonappdata}\ConNgoan'), True, True, True);
  end;
end;
