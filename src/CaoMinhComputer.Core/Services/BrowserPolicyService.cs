using System.Runtime.Versioning;
using CaoMinhComputer.Core.Interfaces;
using Microsoft.Win32;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Chính sách trình duyệt (Chrome, Edge, Brave, Cốc Cốc, Firefox) – CHỈ ghi vào registry của tài khoản con
/// (HKEY_USERS\&lt;SID con&gt;\Software\Policies\...), không đụng tới tài khoản phụ huynh hay HKLM:
/// - Tắt DNS-over-HTTPS để trình duyệt không đi vòng qua file HOSTS.
/// - Bắt buộc SafeSearch (Google, Bing) và YouTube chế độ hạn chế.
/// - Khóa chế độ ẩn danh / InPrivate.
/// - (Tùy chọn) chặn cài/chạy tiện ích mở rộng – lưu ý: cũng tắt các tiện ích đã cài của con.
/// Giá trị gốc được lưu vào ISystemChangeStore (khóa "HKU\&lt;sid&gt;\...") để hoàn tác đúng khi bỏ chọn, tạm tắt hoặc gỡ cài đặt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserPolicyService(ISystemChangeStore store)
{
    public sealed record Options(bool DisableDoH, bool BlockExtensions, bool SafeSearch, bool BlockIncognito)
    {
        public static readonly Options None = new(false, false, false, false);
    }

    internal sealed record Rule(string Path, string Name, object Value, RegistryValueKind Kind, Func<Options, bool> When);

    private static readonly string[] ChromiumRoots =
    [
        @"Software\Policies\Google\Chrome",
        @"Software\Policies\Microsoft\Edge",
        @"Software\Policies\BraveSoftware\Brave",
        @"Software\Policies\CocCoc\Browser"
    ];

    private const string Firefox = @"Software\Policies\Mozilla\Firefox";

    internal static readonly IReadOnlyList<Rule> Rules = BuildRules();

    private static List<Rule> BuildRules()
    {
        var rules = new List<Rule>();
        foreach (var root in ChromiumRoots)
        {
            var isEdge = root.Contains("Edge", StringComparison.Ordinal);
            rules.Add(new(root, "DnsOverHttpsMode", "off", RegistryValueKind.String, o => o.DisableDoH));
            rules.Add(new(root, "ForceGoogleSafeSearch", 1, RegistryValueKind.DWord, o => o.SafeSearch));
            rules.Add(new(root, "ForceBingSafeSearch", 2, RegistryValueKind.DWord, o => o.SafeSearch)); // 2 = nghiêm ngặt
            rules.Add(new(root, "ForceYouTubeRestrict", 2, RegistryValueKind.DWord, o => o.SafeSearch)); // 2 = nghiêm ngặt
            rules.Add(new(root, isEdge ? "InPrivateModeAvailability" : "IncognitoModeAvailability", 1, RegistryValueKind.DWord, o => o.BlockIncognito));
            rules.Add(new(root + @"\ExtensionInstallBlocklist", "1", "*", RegistryValueKind.String, o => o.BlockExtensions));
        }
        rules.Add(new(Firefox + @"\DNSOverHTTPS", "Enabled", 0, RegistryValueKind.DWord, o => o.DisableDoH));
        rules.Add(new(Firefox + @"\DNSOverHTTPS", "Locked", 1, RegistryValueKind.DWord, o => o.DisableDoH));
        rules.Add(new(Firefox, "DisablePrivateBrowsing", 1, RegistryValueKind.DWord, o => o.BlockIncognito));
        rules.Add(new(Firefox + @"\InstallAddonsPermission", "Default", 0, RegistryValueKind.DWord, o => o.BlockExtensions));
        return rules;
    }

    internal static string StoreKey(string sid, Rule r) => $@"HKU\{sid}\{r.Path}\{r.Name}";

    /// <summary>Áp dụng (hoặc gỡ phần đã tắt) chính sách cho một tài khoản con đã có hồ sơ Windows.</summary>
    public void ApplyForUser(string sid, Options options) =>
        UserHive.With(sid, root =>
        {
            foreach (var rule in Rules)
            {
                var storeKey = StoreKey(sid, rule);
                if (rule.When(options))
                {
                    using var key = root.CreateSubKey(rule.Path, writable: true);
                    var existing = key.GetValue(rule.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    store.SaveOriginalIfAbsent(storeKey, existing is not null,
                        existing is null ? null : Serialize(key.GetValueKind(rule.Name), existing));
                    key.SetValue(rule.Name, rule.Value, rule.Kind);
                }
                else
                {
                    Restore(root, storeKey);
                }
            }
            return true;
        });

    /// <summary>Hoàn tác toàn bộ chính sách đã ghi cho tài khoản này.</summary>
    public void RestoreUser(string sid) =>
        UserHive.With(sid, root =>
        {
            foreach (var storeKey in store.Keys().Where(k => k.StartsWith($@"HKU\{sid}\", StringComparison.OrdinalIgnoreCase)).ToList())
                Restore(root, storeKey);
            return true;
        });

    /// <summary>Các tài khoản đang có chính sách do Con Ngoan ghi.</summary>
    public IReadOnlyList<string> UsersWithChanges() =>
        store.Keys()
            .Where(k => k.StartsWith(@"HKU\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Split('\\')[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Dọn chính sách cấp máy (HKLM) do bản thử nghiệm 1.8.0 của bên thứ ba ghi – bản đó áp cho mọi tài khoản kể cả phụ huynh.
    /// </summary>
    public int RestoreLegacyMachinePolicies()
    {
        var keys = store.Keys().Where(k => k.StartsWith(@"HKLM\SOFTWARE\Policies\", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var storeKey in keys)
        {
            var path = storeKey[@"HKLM\".Length..];
            var slash = path.LastIndexOf('\\');
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path[..slash], writable: true);
                if (key is not null) WriteBack(key, path[(slash + 1)..], storeKey);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                continue;
            }
            store.Remove(storeKey);
        }
        return keys.Count;
    }

    private void Restore(RegistryKey root, string storeKey)
    {
        if (!store.TryGetOriginal(storeKey, out _, out _)) return;
        // storeKey = HKU\<sid>\<path>\<name>
        var rest = storeKey[(storeKey.IndexOf('\\', 4) + 1)..];
        var slash = rest.LastIndexOf('\\');
        using (var key = root.OpenSubKey(rest[..slash], writable: true))
        {
            if (key is not null) WriteBack(key, rest[(slash + 1)..], storeKey);
        }
        store.Remove(storeKey);
    }

    private void WriteBack(RegistryKey key, string name, string storeKey)
    {
        store.TryGetOriginal(storeKey, out var existed, out var value);
        if (existed && value is not null && Deserialize(value) is { } original)
            key.SetValue(name, original.Value, original.Kind);
        else
            key.DeleteValue(name, throwOnMissingValue: false);
    }

    internal static string Serialize(RegistryValueKind kind, object value) =>
        kind == RegistryValueKind.DWord ? $"DWord:{Convert.ToInt32(value)}" : $"String:{value}";

    internal static (object Value, RegistryValueKind Kind)? Deserialize(string stored)
    {
        if (stored.StartsWith("DWord:", StringComparison.Ordinal) && int.TryParse(stored[6..], out var n)) return (n, RegistryValueKind.DWord);
        if (stored.StartsWith("String:", StringComparison.Ordinal)) return (stored[7..], RegistryValueKind.String);
        // Bản ghi cũ không có kiểu (bản 1.8.0 của bên thứ ba): số → DWORD, còn lại → chuỗi.
        return int.TryParse(stored, out var legacy) ? (legacy, RegistryValueKind.DWord) : (stored, RegistryValueKind.String);
    }
}
