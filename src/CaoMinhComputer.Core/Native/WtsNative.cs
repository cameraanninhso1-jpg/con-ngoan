using System.Runtime.InteropServices;

namespace CaoMinhComputer.Core.Native;

/// <summary>Windows Terminal Services API – liệt kê/nhắn tin/ngắt/đăng xuất phiên (chỉ dịch vụ chạy SYSTEM mới gọi được cho phiên khác).</summary>
internal static class WtsNative
{
    internal static readonly IntPtr CurrentServer = IntPtr.Zero;

    internal const int WTSActive = 0;
    internal const int WTSUserName = 5;
    internal const int WTSDomainName = 7;
    internal const int WTSSessionInfoEx = 25;
    internal const int WTS_SESSIONSTATE_LOCK = 0;
    internal const int WTS_SESSIONSTATE_UNLOCK = 1;

    internal const int MB_OK = 0x0;
    internal const int MB_ICONWARNING = 0x30;
    internal const int MB_ICONINFORMATION = 0x40;
    internal const int MB_TOPMOST = 0x40000;
    internal const int MB_SETFOREGROUND = 0x10000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSEnumerateSessions(IntPtr hServer, int reserved, int version, out IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int infoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    internal static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSSendMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSSendMessage(IntPtr hServer, int sessionId, string pTitle, int titleLength,
        string pMessage, int messageLength, int style, int timeout, out int pResponse, [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool bWait);

    internal const int WTS_CURRENT_SESSION = -1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSLogoffSession(IntPtr hServer, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out int serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out int clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ProcessIdToSessionId(int processId, out int sessionId);

    internal static string? QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(CurrentServer, sessionId, infoClass, out var buffer, out _)) return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { WTSFreeMemory(buffer); }
    }

    /// <summary>
    /// Đọc cờ khóa màn hình từ WTSINFOEXW. Cấu trúc: DWORD Level; union Data (căn lề 8 byte vì WTSINFOEX_LEVEL1
    /// chứa LARGE_INTEGER) → Data bắt đầu ở offset 8; trong Level1: SessionId(+0), SessionState(+4), SessionFlags(+8)
    /// → SessionFlags ở offset 16.
    /// </summary>
    internal const int SessionFlagsOffset = 16;

    internal static int? QueryLockFlag(int sessionId)
    {
        if (!WTSQuerySessionInformation(CurrentServer, sessionId, WTSSessionInfoEx, out var buffer, out var bytes)) return null;
        try
        {
            if (bytes < SessionFlagsOffset + 4 || Marshal.ReadInt32(buffer, 0) != 1) return null; // chỉ hiểu Level 1
            return Marshal.ReadInt32(buffer, SessionFlagsOffset);
        }
        finally { WTSFreeMemory(buffer); }
    }
}
