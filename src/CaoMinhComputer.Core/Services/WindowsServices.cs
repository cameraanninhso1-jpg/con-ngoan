using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using CaoMinhComputer.Core.Interfaces;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Native;
using Microsoft.Win32;

namespace CaoMinhComputer.Core.Services;

/// <summary>Đọc thông tin phiên bản Windows từ registry.</summary>
public static class WindowsEdition
{
    public static WindowsEditionInfo Detect()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int.TryParse(key?.GetValue("CurrentBuild") as string, out var build);
        return new WindowsEditionInfo
        {
            ProductName = key?.GetValue("ProductName") as string ?? "Windows",
            EditionId = key?.GetValue("EditionID") as string ?? "",
            DisplayVersion = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "",
            Build = build
        };
    }
}

/// <summary>Quản lý tài khoản Windows cục bộ: liệt kê, chuyển Administrator ↔ Standard User.</summary>
public static class LocalAccountService
{
    private static readonly SecurityIdentifier AdminsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier UsersSid = new(WellKnownSidType.BuiltinUsersSid, null);

    private static string GroupName(SecurityIdentifier sid)
    {
        // Tên nhóm phụ thuộc ngôn ngữ Windows ("Administrators", "Quản trị viên"...), nên tra từ SID.
        var name = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        var slash = name.IndexOf('\\');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <param name="includeSystemAccounts">
    /// true = không bỏ các tài khoản hệ thống (Guest, DefaultAccount, WDAGUtilityAccount, Administrator bị vô hiệu hóa).
    /// Dùng cho kiểm tra an toàn "còn quản trị viên nào khác" để không đếm thiếu.
    /// </param>
    public static IReadOnlyList<LocalAccountInfo> GetAccounts(bool includeSystemAccounts = false)
    {
        var admins = GetGroupMemberSids(AdminsSid);
        var current = WindowsIdentity.GetCurrent().User?.Value;
        var list = new List<LocalAccountInfo>();

        var resume = 0;
        var rc = NativeMethods.NetUserEnum(null, 1, NativeMethods.FILTER_NORMAL_ACCOUNT, out var buffer,
            NativeMethods.MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
        if (rc != NativeMethods.NERR_Success && rc != NativeMethods.ERROR_MORE_DATA) throw new Win32Exception(rc);
        try
        {
            var size = Marshal.SizeOf<NativeMethods.USER_INFO_1>();
            for (var i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<NativeMethods.USER_INFO_1>(buffer + i * size);
                SecurityIdentifier sid;
                try
                {
                    sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, info.usri1_name).Translate(typeof(SecurityIdentifier));
                }
                catch (IdentityNotMappedException)
                {
                    continue;
                }

                var rid = sid.Value[(sid.Value.LastIndexOf('-') + 1)..];
                // Bỏ Guest (501), DefaultAccount (503), WDAGUtilityAccount (504).
                var disabled = (info.usri1_flags & NativeMethods.UF_ACCOUNTDISABLE) != 0;
                if (!includeSystemAccounts && (rid is "501" or "503" or "504" || (rid == "500" && disabled))) continue;

                using var profile = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid.Value}");
                list.Add(new LocalAccountInfo
                {
                    Name = info.usri1_name,
                    Sid = sid.Value,
                    IsAdministrator = admins.Contains(sid.Value),
                    IsDisabled = disabled,
                    IsCurrentUser = sid.Value == current,
                    HasProfile = profile is not null
                });
            }
        }
        finally
        {
            NativeMethods.NetApiBufferFree(buffer);
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static HashSet<string> GetGroupMemberSids(SecurityIdentifier groupSid)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resume = IntPtr.Zero;
        var rc = NativeMethods.NetLocalGroupGetMembers(null, GroupName(groupSid), 0, out var buffer,
            NativeMethods.MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
        if (rc != NativeMethods.NERR_Success && rc != NativeMethods.ERROR_MORE_DATA) throw new Win32Exception(rc);
        try
        {
            var size = Marshal.SizeOf<NativeMethods.LOCALGROUP_MEMBERS_INFO_0>();
            for (var i = 0; i < read; i++)
            {
                var m = Marshal.PtrToStructure<NativeMethods.LOCALGROUP_MEMBERS_INFO_0>(buffer + i * size);
                result.Add(new SecurityIdentifier(m.lgrmi0_sid).Value);
            }
        }
        finally
        {
            NativeMethods.NetApiBufferFree(buffer);
        }
        return result;
    }

    /// <summary>Chuyển tài khoản thành Standard User (bỏ khỏi nhóm Administrators, đảm bảo thuộc nhóm Users).</summary>
    public static ApplyResult MakeStandardUser(string sid)
    {
        var accounts = GetAccounts(includeSystemAccounts: true);
        var target = accounts.FirstOrDefault(a => a.Sid == sid);
        if (target is null) return ApplyResult.Fail("Không tìm thấy tài khoản.");
        if (!target.IsAdministrator) return ApplyResult.Ok($"{target.Name} đã là Standard User.");
        if (target.IsCurrentUser)
            return ApplyResult.Fail("Không thể hạ quyền tài khoản đang dùng để quản trị. Hãy đăng nhập bằng tài khoản phụ huynh.");
        var otherAdmins = accounts.Count(a => a.IsAdministrator && !a.IsDisabled && a.Sid != sid);
        if (otherAdmins == 0)
            return ApplyResult.Fail("Đây là tài khoản quản trị duy nhất còn hoạt động; hạ quyền sẽ khóa bạn khỏi máy.");

        ChangeMembership(UsersSid, sid, add: true);
        ChangeMembership(AdminsSid, sid, add: false);
        return ApplyResult.Ok($"Đã chuyển {target.Name} thành Standard User. Có hiệu lực từ lần đăng nhập tiếp theo của tài khoản này.");
    }

    private const string InvalidNameChars = "\"/\\[]:;|=,+*?<>@";

    /// <summary>Kiểm tra tên tài khoản Windows mới theo quy tắc của Windows. Trả về null nếu hợp lệ, ngược lại là lời nhắc.</summary>
    public static string? ValidateNewUserName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Hãy nhập tên tài khoản.";
        if (n.Length > 20) return "Tên tài khoản tối đa 20 ký tự.";
        if (n.EndsWith('.')) return "Tên tài khoản không được kết thúc bằng dấu chấm.";
        if (n.All(ch => ch is '.' or ' ')) return "Tên tài khoản không hợp lệ.";
        var bad = n.FirstOrDefault(ch => InvalidNameChars.Contains(ch) || char.IsControl(ch));
        if (bad != default) return $"Tên tài khoản không được chứa ký tự \"{bad}\". Không dùng: \" / \\ [ ] : ; | = , + * ? < > @";
        if (string.Equals(n, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return "Tên tài khoản không được trùng tên máy tính.";
        return null;
    }

    /// <summary>Kiểm tra mật khẩu của tài khoản con. Cho phép để trống (Windows không hỏi mật khẩu), trả về null nếu hợp lệ.</summary>
    public static string? ValidateNewPassword(string password, string confirm, string userName)
    {
        if (password != confirm) return "Hai lần nhập mật khẩu không khớp.";
        if (password.Length > 127) return "Mật khẩu quá dài (tối đa 127 ký tự).";
        if (password.Length > 0 && userName.Trim().Length > 2 &&
            password.Contains(userName.Trim(), StringComparison.OrdinalIgnoreCase))
            return "Mật khẩu không nên chứa tên tài khoản.";
        return null;
    }

    /// <summary>
    /// Tạo tài khoản Windows cục bộ loại Standard User (chỉ thuộc nhóm Users), mật khẩu không hết hạn.
    /// Trả về SID của tài khoản mới trong <paramref name="sid"/>.
    /// </summary>
    public static ApplyResult CreateStandardUser(string name, string password, string? fullName, out string? sid)
    {
        sid = null;
        var error = ValidateNewUserName(name);
        if (error is not null) return ApplyResult.Fail(error);
        name = name.Trim();

        var info = new NativeMethods.USER_INFO_1
        {
            usri1_name = name,
            usri1_password = password,
            usri1_priv = NativeMethods.USER_PRIV_USER,
            usri1_flags = NativeMethods.UF_SCRIPT | NativeMethods.UF_NORMAL_ACCOUNT | NativeMethods.UF_DONT_EXPIRE_PASSWD,
            usri1_comment = "Tài khoản của con – tạo bởi Con Ngoan"
        };
        var rc = NativeMethods.NetUserAdd(null, 1, ref info, out _);
        switch (rc)
        {
            case NativeMethods.NERR_Success:
                break;
            case NativeMethods.NERR_UserExists:
                return ApplyResult.Fail($"Đã có tài khoản Windows tên \"{name}\". Hãy chọn tên khác hoặc đánh dấu tài khoản đó trong danh sách.");
            case NativeMethods.NERR_GroupExists:
                return ApplyResult.Fail($"\"{name}\" trùng tên một nhóm của Windows. Hãy chọn tên khác.");
            case NativeMethods.NERR_PasswordTooShort:
                return ApplyResult.Fail("Mật khẩu không đạt chính sách mật khẩu của máy (độ dài/độ phức tạp/lịch sử). Hãy dùng mật khẩu dài hơn, có chữ hoa, chữ thường và số.");
            case NativeMethods.NERR_BadUsername:
            case NativeMethods.ERROR_INVALID_ACCOUNT_NAME:
                return ApplyResult.Fail("Windows không chấp nhận tên tài khoản này. Hãy dùng tên ngắn gồm chữ cái và số.");
            case 5:
                return ApplyResult.Fail("Không đủ quyền tạo tài khoản. Hãy mở Con Ngoan bằng tài khoản Administrator.");
            default:
                return ApplyResult.Fail("Windows từ chối tạo tài khoản: " + new Win32Exception(rc).Message + $" (mã {rc}).");
        }

        try
        {
            sid = ((SecurityIdentifier)new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier))).Value;
            // NetUserAdd với USER_PRIV_USER đã thêm vào nhóm Users; gọi lại để chắc chắn (không lỗi nếu đã có).
            ChangeMembership(UsersSid, sid, add: true);
            if (!string.IsNullOrWhiteSpace(fullName) && fullName.Trim() != name)
            {
                var full = new NativeMethods.USER_INFO_1011 { usri1011_full_name = fullName.Trim() };
                NativeMethods.NetUserSetInfo(null, name, 1011, ref full, out _);
            }
        }
        catch (Exception ex)
        {
            return ApplyResult.Fail($"Đã tạo tài khoản \"{name}\" nhưng chưa hoàn tất cấu hình: {ex.Message}");
        }

        return ApplyResult.Ok(password.Length == 0
            ? $"Đã tạo tài khoản Standard User \"{name}\" (không có mật khẩu)."
            : $"Đã tạo tài khoản Standard User \"{name}\".");
    }

    /// <summary>Xóa tài khoản cục bộ (dùng trong kiểm thử). Không xóa thư mục hồ sơ.</summary>
    public static bool DeleteUser(string name)
    {
        var rc = NativeMethods.NetUserDel(null, name);
        return rc is NativeMethods.NERR_Success or NativeMethods.NERR_UserNotFound;
    }

    /// <summary>Cấp lại quyền Administrator (để phụ huynh hoàn tác).</summary>
    public static ApplyResult MakeAdministrator(string sid)
    {
        var target = GetAccounts().FirstOrDefault(a => a.Sid == sid);
        if (target is null) return ApplyResult.Fail("Không tìm thấy tài khoản.");
        ChangeMembership(AdminsSid, sid, add: true);
        return ApplyResult.Ok($"Đã cấp quyền Administrator cho {target.Name}. Có hiệu lực từ lần đăng nhập tiếp theo.");
    }

    private static void ChangeMembership(SecurityIdentifier group, string memberSid, bool add)
    {
        var sid = new SecurityIdentifier(memberSid);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            var info = new NativeMethods.LOCALGROUP_MEMBERS_INFO_0 { lgrmi0_sid = ptr };
            var rc = add
                ? NativeMethods.NetLocalGroupAddMembers(null, GroupName(group), 0, ref info, 1)
                : NativeMethods.NetLocalGroupDelMembers(null, GroupName(group), 0, ref info, 1);
            if (rc is NativeMethods.NERR_Success or NativeMethods.ERROR_MEMBER_IN_ALIAS or NativeMethods.ERROR_MEMBER_NOT_IN_ALIAS)
                return;
            throw new Win32Exception(rc);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

/// <summary>Truy cập registry (HKCU) của một tài khoản khác: dùng hive đã nạp nếu người đó đang đăng nhập, nếu không thì nạp tạm NTUSER.DAT.</summary>
public static class UserHive
{
    public static T With<T>(string sid, Func<RegistryKey, T> action)
    {
        using (var loaded = Registry.Users.OpenSubKey(sid, writable: true))
        {
            if (loaded is not null) return action(loaded);
        }

        string? profilePath;
        using (var profile = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}"))
            profilePath = profile?.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrEmpty(profilePath))
            throw new InvalidOperationException("Tài khoản này chưa đăng nhập Windows lần nào nên chưa có hồ sơ. Hãy đăng nhập tài khoản đó một lần rồi áp dụng lại.");

        var ntuser = Path.Combine(Environment.ExpandEnvironmentVariables(profilePath), "NTUSER.DAT");
        if (!File.Exists(ntuser)) throw new FileNotFoundException("Không tìm thấy NTUSER.DAT của tài khoản.", ntuser);

        NativeMethods.EnablePrivilege("SeBackupPrivilege");
        NativeMethods.EnablePrivilege("SeRestorePrivilege");
        var mount = "ConNgoanTmp_" + sid;
        // Một hive chỉ nạp được một lần: các luồng trong cùng tiến trình xếp hàng bằng lock; giao diện và dịch vụ
        // (hai tiến trình) có thể đụng nhau → chờ bên kia gỡ hive rồi thử lại (tối đa ~15 giây).
        lock (HiveLocks.GetOrAdd(sid, _ => new object()))
        {
            var rc = 0;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                using (var loadedNow = Registry.Users.OpenSubKey(sid, writable: true))
                {
                    if (loadedNow is not null) return action(loadedNow); // con vừa đăng nhập
                }
                rc = NativeMethods.RegLoadKey(NativeMethods.HKEY_USERS, mount, ntuser);
                if (rc == 0) break;
                Thread.Sleep(250);
            }
            if (rc != 0) throw new Win32Exception(rc, "Không nạp được registry của tài khoản (có thể đang bị khóa).");
            return WithMounted(mount, action);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> HiveLocks = new(StringComparer.OrdinalIgnoreCase);

    private static T WithMounted<T>(string mount, Func<RegistryKey, T> action)
    {
        try
        {
            using var key = Registry.Users.OpenSubKey(mount, writable: true)
                            ?? throw new InvalidOperationException("Không mở được registry đã nạp.");
            return action(key);
        }
        finally
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (NativeMethods.RegUnLoadKey(NativeMethods.HKEY_USERS, mount) == 0) break;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(200);
            }
        }
    }
}

/// <summary>Trạng thái những gì Con Ngoan đã ghi vào DisallowRun của một tài khoản (để gỡ đúng phần của mình).</summary>
public sealed class DisallowRunState
{
    public List<string> OwnedValueNames { get; set; } = [];
    public List<string> Executables { get; set; } = [];
    public bool FlagTouched { get; set; }
    public bool FlagExisted { get; set; }
    public int? FlagOriginal { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static DisallowRunState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DisallowRunState();
        try { return JsonSerializer.Deserialize<DisallowRunState>(json) ?? new DisallowRunState(); }
        catch (JsonException) { return new DisallowRunState(); }
    }
}

/// <summary>
/// Chặn ứng dụng mức cơ bản qua chính sách Explorer "DisallowRun" trong HKCU của tài khoản trẻ.
/// Có hiệu lực trên mọi edition (kể cả Home) nhưng chỉ chặn chương trình mở qua Explorer
/// (Start menu, Desktop, File Explorer). Không chặn chương trình được tiến trình khác khởi chạy.
/// Mức đầy đủ (AppLocker/WDAC) thuộc Giai đoạn 3.
/// </summary>
public static class DisallowRunProvider
{
    private const string PolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    public static DisallowRunState Apply(string sid, IReadOnlyCollection<string> executables, DisallowRunState previous)
    {
        var exes = executables
            .Select(ProtectedProcesses.NormalizeExeName)
            .Where(e => e is not null && !ProtectedProcesses.IsProtected(e))
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return UserHive.With(sid, root =>
        {
            var state = new DisallowRunState
            {
                FlagTouched = previous.FlagTouched,
                FlagExisted = previous.FlagExisted,
                FlagOriginal = previous.FlagOriginal
            };

            using var policy = root.CreateSubKey(PolicyPath, writable: true);
            using (var list = policy.CreateSubKey("DisallowRun", writable: true))
            {
                foreach (var name in previous.OwnedValueNames)
                    list.DeleteValue(name, throwOnMissingValue: false);

                var used = new HashSet<string>(list.GetValueNames(), StringComparer.OrdinalIgnoreCase);
                var next = 1;
                foreach (var exe in exes)
                {
                    while (used.Contains(next.ToString())) next++;
                    var valueName = next.ToString();
                    list.SetValue(valueName, exe, RegistryValueKind.String);
                    used.Add(valueName);
                    state.OwnedValueNames.Add(valueName);
                    state.Executables.Add(exe);
                }
            }

            if (exes.Count > 0)
            {
                if (!state.FlagTouched)
                {
                    var existing = policy.GetValue("DisallowRun");
                    state.FlagTouched = true;
                    state.FlagExisted = existing is not null;
                    state.FlagOriginal = existing is int i ? i : null;
                }
                policy.SetValue("DisallowRun", 1, RegistryValueKind.DWord);
            }
            else if (state.FlagTouched)
            {
                if (state.FlagExisted && state.FlagOriginal is int original)
                    policy.SetValue("DisallowRun", original, RegistryValueKind.DWord);
                else
                    policy.DeleteValue("DisallowRun", throwOnMissingValue: false);
                state.FlagTouched = false;
                state.FlagExisted = false;
                state.FlagOriginal = null;

                // Xóa khóa con DisallowRun nếu rỗng và do ta tạo ra.
                using var check = policy.OpenSubKey("DisallowRun");
                var empty = check is not null && check.ValueCount == 0 && check.SubKeyCount == 0;
                check?.Dispose();
                if (empty) policy.DeleteSubKey("DisallowRun", throwOnMissingSubKey: false);
            }
            return state;
        });
    }
}

/// <summary>
/// Chính sách liên quan tới cài đặt phần mềm dựa trên cơ chế chuẩn của Windows.
/// Không tắt UAC, Defender hay Windows Update.
/// </summary>
public sealed class InstallPolicyService
{
    private const string SystemPolicy = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string InstallerPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Installer";
    public const string KeyConsentUser = "HKLM\\" + SystemPolicy + "\\ConsentPromptBehaviorUser";
    public const string KeyDisableUserInstalls = "HKLM\\" + InstallerPolicy + "\\DisableUserInstalls";

    /// <summary>3 = hỏi thông tin đăng nhập quản trị (mặc định Windows). 0 = tự động từ chối yêu cầu nâng quyền.</summary>
    public const int ConsentPromptCredentials = 3;
    public const int ConsentAutoDeny = 0;

    private readonly ISystemChangeStore _store;

    public InstallPolicyService(ISystemChangeStore store) => _store = store;

    public bool IsUacEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SystemPolicy);
        return key?.GetValue("EnableLUA") is not int v || v != 0;
    }

    public int GetConsentPromptBehaviorUser()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SystemPolicy);
        return key?.GetValue("ConsentPromptBehaviorUser") is int v ? v : ConsentPromptCredentials;
    }

    public bool IsUserInstallsDisabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallerPolicy);
        return key?.GetValue("DisableUserInstalls") is int v && v == 1;
    }

    public void SetConsentPromptBehaviorUser(int value)
    {
        if (value is not (ConsentPromptCredentials or ConsentAutoDeny)) throw new ArgumentOutOfRangeException(nameof(value));
        using var key = Registry.LocalMachine.CreateSubKey(SystemPolicy, writable: true);
        var existing = key.GetValue("ConsentPromptBehaviorUser");
        _store.SaveOriginalIfAbsent(KeyConsentUser, existing is not null, existing?.ToString());
        key.SetValue("ConsentPromptBehaviorUser", value, RegistryValueKind.DWord);
    }

    public void SetUserInstallsDisabled(bool disabled)
    {
        using var key = Registry.LocalMachine.CreateSubKey(InstallerPolicy, writable: true);
        var existing = key.GetValue("DisableUserInstalls");
        if (disabled)
        {
            _store.SaveOriginalIfAbsent(KeyDisableUserInstalls, existing is not null, existing?.ToString());
            key.SetValue("DisableUserInstalls", 1, RegistryValueKind.DWord);
        }
        else
        {
            // Chỉ hoàn tác thay đổi do Con Ngoan tạo; chính sách do phần mềm/quản trị khác đặt được giữ nguyên.
            RestoreOne(KeyDisableUserInstalls);
        }
    }

    public bool IsUserInstallsManagedByUs() => _store.TryGetOriginal(KeyDisableUserInstalls, out _, out _);

    /// <summary>Khôi phục đúng giá trị gốc của mọi thiết lập do Con Ngoan thay đổi.</summary>
    public void RestoreAll()
    {
        foreach (var k in _store.Keys().Where(k => k.StartsWith("HKLM\\", StringComparison.Ordinal)))
            RestoreOne(k);
    }

    private void RestoreOne(string storeKey)
    {
        if (!_store.TryGetOriginal(storeKey, out var existed, out var value)) return;
        var path = storeKey["HKLM\\".Length..];
        var slash = path.LastIndexOf('\\');
        var subKey = path[..slash];
        var valueName = path[(slash + 1)..];
        using (var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true))
        {
            if (existed && int.TryParse(value, out var number))
                key.SetValue(valueName, number, RegistryValueKind.DWord);
            else
                key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        _store.Remove(storeKey);
    }
}

/// <summary>Lấy SID chủ sở hữu tiến trình (dùng bởi dịch vụ nền).</summary>
public static class ProcessOwner
{
    public static string? GetOwnerSid(int processId) => NativeMethods.GetProcessOwnerSid(processId);
}
