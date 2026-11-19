using Microsoft.Win32;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Chống can thiệp trên tài khoản con (chỉ ghi vào registry riêng của con, không ảnh hưởng phụ huynh):
/// khóa Task Manager, Registry Editor và cửa sổ Command Prompt (DisableCMD=2: vẫn chạy được file .bat của trường/học tập).
/// Tài khoản con là Standard User nên vốn không đổi được giờ hệ thống; thời gian dùng máy đếm bằng đồng hồ đơn điệu
/// nên chỉnh đồng hồ/múi giờ không làm sai số phút.
/// Lỗi (ví dụ registry đang bị khóa) được ném ra để bên gọi ghi nhật ký.
/// </summary>
public static class AntiTamperService
{
    private const string SystemPolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string WinSystemPolicyPath = @"Software\Policies\Microsoft\Windows\System";

    public static void Apply(string sid) =>
        UserHive.With(sid, root =>
        {
            using (var sys = root.CreateSubKey(SystemPolicyPath, writable: true))
            {
                sys.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                sys.SetValue("DisableRegistryTools", 1, RegistryValueKind.DWord);
            }
            using (var winSys = root.CreateSubKey(WinSystemPolicyPath, writable: true))
                winSys.SetValue("DisableCMD", 2, RegistryValueKind.DWord);
            return true;
        });

    public static void Remove(string sid) =>
        UserHive.With(sid, root =>
        {
            using (var sys = root.OpenSubKey(SystemPolicyPath, writable: true))
            {
                sys?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
                sys?.DeleteValue("DisableRegistryTools", throwOnMissingValue: false);
            }
            using (var winSys = root.OpenSubKey(WinSystemPolicyPath, writable: true))
                winSys?.DeleteValue("DisableCMD", throwOnMissingValue: false);
            return true;
        });
}
