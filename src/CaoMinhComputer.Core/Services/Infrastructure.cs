using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using CaoMinhComputer.Core.Native;

namespace CaoMinhComputer.Core.Services;

/// <summary>Đường dẫn dữ liệu cục bộ. Dữ liệu nằm ở %ProgramData%\ConNgoan với ACL chỉ cho SYSTEM + Administrators.</summary>
public static class AppPaths
{
    public const string ServiceName = "ConNgoanService";

    private static string? _overrideDataDir;

    /// <summary>Cho phép kiểm thử dùng thư mục tạm.</summary>
    public static void OverrideDataDir(string? dir) => _overrideDataDir = dir;

    public static string DataDir => _overrideDataDir ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConNgoan");

    public static string DatabasePath => Path.Combine(DataDir, "caominh.db");
    public static string BackupDir => Path.Combine(DataDir, "backups");
    public static string ExportDir => Path.Combine(DataDir, "exports");

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator) || id.IsSystem;
    }

    /// <summary>Tạo thư mục dữ liệu và đặt ACL chặt: chỉ SYSTEM và Administrators có quyền (tài khoản trẻ không đọc/sửa được).</summary>
    public static void EnsureDataDirectory()
    {
        var di = Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        if (_overrideDataDir is not null || !IsElevated()) return;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        di.SetAccessControl(security);
    }
}

/// <summary>Băm mật khẩu bằng PBKDF2-HMAC-SHA256, salt ngẫu nhiên 16 byte, 600.000 vòng (khuyến nghị OWASP 2023+).</summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Kiểm tra độ mạnh tối thiểu. Trả về null nếu hợp lệ, ngược lại là thông báo lỗi.</summary>
    public static string? ValidateStrength(string password)
    {
        if (password.Length < 8) return "Mật khẩu cần ít nhất 8 ký tự.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Mật khẩu cần có cả chữ và số.";
        return null;
    }
}

/// <summary>Làm mới bộ nhớ đệm DNS của Windows sau khi sửa HOSTS.</summary>
public static class DnsCache
{
    public static bool Flush()
    {
        try
        {
            if (NativeMethods.DnsFlushResolverCache()) return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            p?.WaitForExit(10_000);
            return p?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Tiến trình hệ thống/bảo mật/trợ năng không bao giờ được phép chặn.</summary>
public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "svchost.exe", "csrss.exe", "winlogon.exe", "wininit.exe", "lsass.exe", "services.exe",
        "smss.exe", "dwm.exe", "sihost.exe", "ctfmon.exe", "fontdrvhost.exe", "conhost.exe", "runtimebroker.exe",
        "taskmgr.exe", "taskhostw.exe", "logonui.exe", "userinit.exe", "consent.exe", "dllhost.exe",
        "searchhost.exe", "searchapp.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "textinputhost.exe", "applicationframehost.exe", "systemsettings.exe", "securityhealthservice.exe",
        "securityhealthsystray.exe", "msmpeng.exe", "nissrv.exe", "mpcmdrun.exe", "smartscreen.exe",
        "narrator.exe", "magnify.exe", "osk.exe", "sethc.exe", "utilman.exe", "atbroker.exe", "voiceaccess.exe",
        "msedgewebview2.exe", "wuauclt.exe", "trustedinstaller.exe", "tiworker.exe", "mmc.exe", "regedit.exe",
        "caominhcomputer.exe", "caominhcomputer.service.exe", "unins000.exe"
    };

    public static bool IsProtected(string processName)
    {
        var name = Path.GetFileName(processName.Trim());
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return Names.Contains(name);
    }

    /// <summary>Chuẩn hóa "discord" / "C:\...\Discord.exe" thành "Discord.exe". Trả về null nếu không hợp lệ.</summary>
    public static string? NormalizeExeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var name = Path.GetFileName(input.Trim().Trim('"'));
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return name.Length > 4 ? name : null;
    }
}
