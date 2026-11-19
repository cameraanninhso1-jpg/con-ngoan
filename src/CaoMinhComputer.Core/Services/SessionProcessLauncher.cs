using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Khởi chạy tiến trình trong phiên người dùng tương tác từ dịch vụ nền (LocalSystem)
/// và giám sát tự động khởi động lại ConNgoan.Agent.exe nếu con cố tình tắt.
/// </summary>
public static class SessionProcessLauncher
{
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    private const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    public static bool StartProcessInSession(int sessionId, string exePath)
    {
        if (!File.Exists(exePath)) return false;
        var userToken = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken((uint)sessionId, out userToken)) return false;
            // Biến môi trường của chính tài khoản con (TEMP, USERPROFILE...) – không dùng môi trường của SYSTEM.
            if (!CreateEnvironmentBlock(out environment, userToken, false)) environment = IntPtr.Zero;

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"Winsta0\Default";

            var dir = Path.GetDirectoryName(exePath);
            var success = CreateProcessAsUser(
                userToken,
                exePath,
                $"\"{exePath}\"",
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NORMAL_PRIORITY_CLASS | (environment != IntPtr.Zero ? CREATE_UNICODE_ENVIRONMENT : 0),
                environment,
                dir,
                ref si,
                out var pi);

            if (success)
            {
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
            }
            return success;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    /// <summary>Kiểm tra xem Agent có đang chạy trong phiên của con không; nếu không, tự động khởi động lại.</summary>
    public static void SuperviseAgentInChildSessions(IReadOnlyCollection<string> childSids, string agentExePath)
    {
        if (childSids.Count == 0 || !File.Exists(agentExePath)) return;
        try
        {
            var sessions = WindowsSessions.Enumerate()
                .Where(s => s.IsActive && !s.IsLocked && s.Sid != null && childSids.Contains(s.Sid, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (sessions.Count == 0) return;

            var runningSessions = Process.GetProcessesByName("ConNgoan.Agent")
                .Select(p => { try { return p.SessionId; } catch { return -1; } })
                .Where(id => id > 0)
                .ToHashSet();

            foreach (var s in sessions)
            {
                if (!runningSessions.Contains(s.SessionId))
                {
                    StartProcessInSession(s.SessionId, agentExePath);
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi giám sát
        }
    }
}
