using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Native;

namespace CaoMinhComputer.Core.Services;

/// <summary>Theo dõi và điều khiển các phiên đăng nhập Windows (dùng bởi dịch vụ nền chạy SYSTEM).</summary>
public static class WindowsSessions
{
    public static IReadOnlyList<UserSession> Enumerate()
    {
        var list = new List<UserSession>();
        if (!WtsNative.WTSEnumerateSessions(WtsNative.CurrentServer, 0, 1, out var buffer, out var count)) return list;
        try
        {
            var size = Marshal.SizeOf<WtsNative.WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsNative.WTS_SESSION_INFO>(buffer + i * size);
                if (info.SessionId == 0) continue; // phiên dịch vụ
                var user = WtsNative.QueryString(info.SessionId, WtsNative.WTSUserName);
                if (string.IsNullOrEmpty(user)) continue;
                var domain = WtsNative.QueryString(info.SessionId, WtsNative.WTSDomainName);
                string? sid = null;
                try
                {
                    var account = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
                    sid = ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value;
                }
                catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
                {
                    // Không xác định được SID – bỏ qua.
                }
                var lockFlag = WtsNative.QueryLockFlag(info.SessionId);
                list.Add(new UserSession(info.SessionId, user, sid,
                    info.State == WtsNative.WTSActive,
                    lockFlag == WtsNative.WTS_SESSIONSTATE_LOCK));
            }
        }
        finally
        {
            WtsNative.WTSFreeMemory(buffer);
        }
        return list;
    }

    /// <summary>Hiện hộp thông báo trong phiên của con (không chờ người dùng bấm).</summary>
    public static bool SendMessage(int sessionId, string title, string message, int timeoutSeconds = 60)
    {
        return WtsNative.WTSSendMessage(WtsNative.CurrentServer, sessionId,
            title, title.Length * 2, message, message.Length * 2,
            WtsNative.MB_OK | WtsNative.MB_ICONWARNING | WtsNative.MB_TOPMOST | WtsNative.MB_SETFOREGROUND,
            timeoutSeconds, out _, false);
    }

    /// <summary>Ngắt phiên về màn hình đăng nhập – ứng dụng vẫn chạy, không mất dữ liệu.</summary>
    public static bool Disconnect(int sessionId) => WtsNative.WTSDisconnectSession(WtsNative.CurrentServer, sessionId, false);

    /// <summary>
    /// "Đổi người dùng": ngắt phiên hiện tại để Windows hiện màn hình đăng nhập, nơi chọn tài khoản khác.
    /// Ứng dụng đang mở của phiên này vẫn giữ nguyên. Nếu không ngắt được (chính sách hạn chế) thì khóa máy –
    /// màn hình khóa cũng có danh sách tài khoản để chuyển.
    /// </summary>
    public static bool SwitchUser()
    {
        if (WtsNative.WTSDisconnectSession(WtsNative.CurrentServer, WtsNative.WTS_CURRENT_SESSION, false)) return true;
        return WtsNative.LockWorkStation();
    }

    /// <summary>Chính sách HideFastUserSwitching = 1 thì màn hình đăng nhập không cho chọn tài khoản khác khi phiên này còn mở.</summary>
    public static bool IsFastUserSwitchingHidden()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("HideFastUserSwitching") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Đăng xuất phiên – ứng dụng bị đóng, có thể mất dữ liệu chưa lưu.</summary>
    public static bool Logoff(int sessionId) => WtsNative.WTSLogoffSession(WtsNative.CurrentServer, sessionId, false);

    /// <summary>Trình duyệt phổ biến tại Việt Nam (Cốc Cốc dùng browser.exe).</summary>
    public static readonly string[] BrowserProcessNames =
        ["chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "browser", "vivaldi", "iexplore"];

    /// <summary>Đóng trình duyệt thuộc các phiên chỉ định. Trả về số tiến trình đã đóng.</summary>
    public static int CloseBrowsers(IReadOnlySet<int> sessionIds)
    {
        var closed = 0;
        foreach (var name in BrowserProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        if (!sessionIds.Contains(p.SessionId)) continue;
                        p.Kill(entireProcessTree: true);
                        closed++;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Đã thoát hoặc không đóng được.
                    }
                }
            }
        }
        return closed;
    }

    /// <summary>Thông tin tiến trình phía client của named pipe (phía server dùng để xác thực Agent).</summary>
    public sealed record PipeClient(int ProcessId, int SessionId, string? ImagePath, string? OwnerSid);

    public static PipeClient? GetPipeClient(SafeHandle pipeHandle)
    {
        if (!WtsNative.GetNamedPipeClientProcessId(pipeHandle.DangerousGetHandle(), out var pid)) return null;
        if (!WtsNative.ProcessIdToSessionId(pid, out var session)) return null;
        return new PipeClient(pid, session, NativeMethods.GetProcessImagePath(pid), NativeMethods.GetProcessOwnerSid(pid));
    }

    /// <summary>Kiểm tra đầu bên kia của named pipe là đúng tiến trình mong đợi (chống giả mạo pipe).</summary>
    public static bool IsPipeServer(SafeHandle pipeHandle, string expectedExePath)
    {
        if (!WtsNative.GetNamedPipeServerProcessId(pipeHandle.DangerousGetHandle(), out var pid)) return false;
        var path = NativeMethods.GetProcessImagePath(pid);
        return path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedExePath), StringComparison.OrdinalIgnoreCase);
    }
}
