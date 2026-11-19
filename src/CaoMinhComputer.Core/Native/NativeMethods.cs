using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CaoMinhComputer.Core.Native;

internal static class NativeMethods
{
    // ---------- DNS ----------
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DnsFlushResolverCache();

    // ---------- NetAPI (tài khoản cục bộ) ----------
    internal const int NERR_Success = 0;
    internal const int ERROR_MORE_DATA = 234;
    internal const int ERROR_MEMBER_IN_ALIAS = 1378;
    internal const int ERROR_MEMBER_NOT_IN_ALIAS = 1377;
    internal const int FILTER_NORMAL_ACCOUNT = 0x0002;
    internal const int MAX_PREFERRED_LENGTH = -1;
    internal const int UF_ACCOUNTDISABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_1
    {
        public string usri1_name;
        public string usri1_password;
        public int usri1_password_age;
        public int usri1_priv;
        public string usri1_home_dir;
        public string usri1_comment;
        public int usri1_flags;
        public string usri1_script_path;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LOCALGROUP_MEMBERS_INFO_0
    {
        public IntPtr lgrmi0_sid;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserEnum(string? servername, int level, int filter, out IntPtr bufptr,
        int prefmaxlen, out int entriesread, out int totalentries, ref int resumeHandle);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupGetMembers(string? servername, string localgroupname, int level,
        out IntPtr bufptr, int prefmaxlen, out int entriesread, out int totalentries, ref IntPtr resumehandle);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupAddMembers(string? servername, string groupname, int level,
        ref LOCALGROUP_MEMBERS_INFO_0 buf, int totalentries);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetLocalGroupDelMembers(string? servername, string groupname, int level,
        ref LOCALGROUP_MEMBERS_INFO_0 buf, int totalentries);

    [DllImport("netapi32.dll")]
    internal static extern int NetApiBufferFree(IntPtr buffer);

    internal const int USER_PRIV_USER = 1;
    internal const int UF_SCRIPT = 0x0001;
    internal const int UF_NORMAL_ACCOUNT = 0x0200;
    internal const int UF_DONT_EXPIRE_PASSWD = 0x10000;
    internal const int NERR_UserExists = 2224;
    internal const int NERR_GroupExists = 2223;
    internal const int NERR_PasswordTooShort = 2245;
    internal const int NERR_BadUsername = 2202;
    internal const int NERR_UserNotFound = 2221;
    internal const int ERROR_INVALID_ACCOUNT_NAME = 1315;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_1011
    {
        public string usri1011_full_name;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserAdd(string? servername, int level, ref USER_INFO_1 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserSetInfo(string? servername, string username, int level, ref USER_INFO_1011 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUserDel(string? servername, string username);

    // ---------- Registry hive ----------
    internal static readonly IntPtr HKEY_USERS = new(unchecked((int)0x80000003));

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

    // ---------- Token / privileges ----------
    internal const int TOKEN_QUERY = 0x0008;
    internal const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const int SE_PRIVILEGE_ENABLED = 0x0002;
    internal const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const int TokenUser = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    internal static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var err = Marshal.GetLastWin32Error();
            if (err != 0) throw new Win32Exception(err, $"Không bật được quyền {name}.");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder exeName, ref int size);

    /// <summary>Đường dẫn file exe của tiến trình (chỉ cần quyền truy vấn giới hạn).</summary>
    internal static string? GetProcessImagePath(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Lấy SID chủ sở hữu của một tiến trình (cần quyền SYSTEM/Administrator với tiến trình của người khác).</summary>
    internal static string? GetProcessOwnerSid(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
            try
            {
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var length);
                if (length <= 0) return null;
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, buffer, length, out _)) return null;
                    var sidPtr = Marshal.ReadIntPtr(buffer);
                    return new SecurityIdentifier(sidPtr).Value;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
