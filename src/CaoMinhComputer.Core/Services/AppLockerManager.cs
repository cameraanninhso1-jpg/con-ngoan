using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>Thông tin một tài khoản con dùng để tạo quy tắc AppLocker.</summary>
public sealed record AppLockerChild(string Sid, string? ProfilePath);

/// <summary>
/// Chặn ứng dụng bằng AppLocker (Giai đoạn 3) – quy tắc chỉ áp dụng cho SID của tài khoản con:
/// - Chặn ứng dụng trong danh sách theo tên file ở mọi thư mục (kể cả khi được launcher khởi chạy).
/// - Chế độ nghiêm ngặt: con chỉ chạy được file .exe trong %WINDIR%, %PROGRAMFILES% (do phụ huynh cài) và các thư mục được phép;
///   chặn game portable, file tải về, Desktop/Downloads/USB.
///
/// Cơ chế áp dụng: AppLocker CSP qua MDM Bridge WMI (giống Intune), chạy bằng quyền SYSTEM trong dịch vụ nền.
/// Đã kiểm chứng trong Windows Sandbox: Set-AppLockerPolicy (Local GPO) ghi được chính sách nhưng KHÔNG được thực thi,
/// còn AppLocker CSP có hiệu lực ngay, kể cả với file đã từng chạy. CSP được Microsoft hỗ trợ trên Pro/Enterprise/Education.
/// An toàn: toàn bộ quy tắc nằm trong một nhóm riêng "Con Ngoan" (gỡ là xóa đúng nhóm đó), không đụng chính sách
/// Group Policy/Intune khác, không bao giờ tạo quy tắc chặn cho Administrators/Everyone.
/// </summary>
public static class AppLockerManager
{
    public const string RulePrefix = "ConNgoan:";
    public const string MdmGrouping = "ConNgoan";
    private const string Everyone = "S-1-1-0";
    private const string MdmNamespace = @"root\cimv2\mdm\dmmap";
    private const string MdmClass = "MDM_AppLocker_ApplicationLaunchRestrictions01_EXE03";
    private const string MdmParentId = "./Vendor/MSFT/AppLocker/ApplicationLaunchRestrictions/" + MdmGrouping;

    /// <summary>AppLocker CSP có trên Pro, Enterprise, Education; Windows Home không có.</summary>
    public static bool IsSupported(WindowsEditionInfo edition) => edition.IsEnterpriseOrEducation || edition.IsPro;

    public static string SupportText(WindowsEditionInfo edition) =>
        IsSupported(edition)
            ? $"AppLocker (CSP) được hỗ trợ trên {edition.FriendlyName}; dịch vụ nền áp dụng quy tắc bằng quyền SYSTEM."
            : $"{edition.FriendlyName} không có AppLocker. Đang dùng mức cơ bản (DisallowRun + dịch vụ tự đóng ứng dụng).";

    /// <summary>Tạo quy tắc của Con Ngoan cho bộ sưu tập Exe.</summary>
    public static IReadOnlyList<XElement> BuildRules(IReadOnlyList<AppLockerChild> children, IReadOnlyCollection<string> blockedExes,
        bool strict, IReadOnlyCollection<string> allowedFolders, bool includeAllowAll)
    {
        var rules = new List<XElement>();
        if (includeAllowAll)
        {
            // Một bộ sưu tập AppLocker đã bật sẽ chặn mặc định mọi thứ không được cho phép –
            // quy tắc này giữ nguyên quyền chạy của mọi người, chỉ các quy tắc Deny bên dưới tác động lên con.
            rules.Add(PathRule("allow-all", "cho phép mặc định (mọi người)", Everyone, "Allow", "*", []));
        }

        var exes = blockedExes
            .Select(ProtectedProcesses.NormalizeExeName)
            .Where(e => e is not null && !ProtectedProcesses.IsProtected(e))
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var child in children)
        {
            foreach (var exe in exes)
                rules.Add(PathRule($"app|{child.Sid}|{exe}", $"chặn {exe}", child.Sid, "Deny", "*\\" + exe, []));

            if (!strict) continue;
            var exceptions = new List<string> { "%WINDIR%\\*", "%PROGRAMFILES%\\*" };
            foreach (var folder in allowedFolders)
            {
                var path = NormalizeFolder(folder, child.ProfilePath);
                if (path is not null && !exceptions.Contains(path, StringComparer.OrdinalIgnoreCase)) exceptions.Add(path);
            }
            rules.Add(PathRule($"strict|{child.Sid}", "chế độ nghiêm ngặt – chỉ chạy ứng dụng phụ huynh đã cài", child.Sid, "Deny", "*", exceptions));
        }
        return rules;
    }

    /// <summary>
    /// "C:\Users\%USERNAME%\AppData\Roaming\Zoom" → thư mục + "\*". Hỗ trợ biến %USERPROFILE% (thay bằng hồ sơ của con).
    /// Trả về null nếu không hợp lệ hoặc quá rộng (ổ đĩa gốc, thư mục hồ sơ).
    /// </summary>
    public static string? NormalizeFolder(string? folder, string? childProfile)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var f = folder.Trim().Trim('"');
        if (childProfile is not null)
            f = f.Replace("%USERPROFILE%", childProfile, StringComparison.OrdinalIgnoreCase);
        else if (f.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            return null;
        f = f.TrimEnd('*').TrimEnd('\\');
        if (f.Length <= 3 || f.Contains('*') || f.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null; // "C:" hay "C:\"
        if (childProfile is not null && string.Equals(f, childProfile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
        return f + "\\*";
    }

    private static XElement PathRule(string key, string description, string sid, string action, string path, IReadOnlyList<string> exceptions)
    {
        var rule = new XElement("FilePathRule",
            new XAttribute("Id", StableGuid(key).ToString()),
            new XAttribute("Name", $"{RulePrefix} {description}"),
            new XAttribute("Description", "Tạo bởi Con Ngoan – gỡ trong ứng dụng hoặc khi gỡ cài đặt."),
            new XAttribute("UserOrGroupSid", sid),
            new XAttribute("Action", action),
            new XElement("Conditions", new XElement("FilePathCondition", new XAttribute("Path", path))));
        if (exceptions.Count > 0)
            rule.Add(new XElement("Exceptions", exceptions.Select(e => new XElement("FilePathCondition", new XAttribute("Path", e)))));
        return rule;
    }

    /// <summary>GUID ổn định theo khóa → áp dụng lại không sinh quy tắc trùng.</summary>
    public static Guid StableGuid(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ConNgoan|" + key));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static bool IsOurs(XElement rule) =>
        ((string?)rule.Attribute("Name"))?.StartsWith(RulePrefix, StringComparison.Ordinal) == true;

    /// <summary>Nội dung "Policy" của AppLocker CSP: một RuleCollection Exe đã bật.</summary>
    public static string BuildRuleCollection(IReadOnlyList<XElement> rules) =>
        new XElement("RuleCollection",
            new XAttribute("Type", "Exe"),
            new XAttribute("EnforcementMode", "Enabled"),
            rules).ToString(SaveOptions.DisableFormatting);

    /// <summary>Chính sách Group Policy cục bộ/tổ chức có quy tắc Exe riêng không (khi đó không thêm quy tắc "cho phép mọi người").</summary>
    public static bool HasForeignExeRules(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        var exe = XDocument.Parse(xml).Root?.Elements("RuleCollection").FirstOrDefault(e => (string?)e.Attribute("Type") == "Exe");
        return exe?.Elements().Any(r => !IsOurs(r)) == true;
    }

    /// <summary>Đếm quy tắc của Con Ngoan trong một chính sách (AppLockerPolicy hoặc RuleCollection, có thể đã mã hóa XML).</summary>
    public static int CountOurRules(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return 0;
        var text = xml.TrimStart().StartsWith("&lt;", StringComparison.Ordinal) ? WebUtility.HtmlDecode(xml) : xml;
        try
        {
            return XElement.Parse(text).DescendantsAndSelf().Count(e => e.Attribute("UserOrGroupSid") is not null && IsOurs(e));
        }
        catch (System.Xml.XmlException)
        {
            return 0;
        }
    }

    // ---------------- Thao tác hệ thống (chỉ dịch vụ nền chạy SYSTEM) ----------------

    public static bool IsRunningAsSystem()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.IsSystem;
    }

    /// <summary>
    /// Áp dụng quy tắc cho các tài khoản con qua AppLocker CSP. Không có gì cần chặn = gỡ nhóm quy tắc của Con Ngoan.
    /// Phải chạy bằng SYSTEM (dịch vụ nền); giao diện gửi yêu cầu qua Named Pipe.
    /// </summary>
    public static ApplyResult Apply(IReadOnlyList<AppLockerChild> children, IReadOnlyCollection<string> blockedExes, bool strict,
        IReadOnlyCollection<string> allowedFolders)
    {
        if (!IsRunningAsSystem())
            return ApplyResult.Fail("AppLocker chỉ được áp dụng bởi dịch vụ nền Con Ngoan (quyền SYSTEM).");

        var wantRules = children.Count > 0 && (strict || blockedExes.Count > 0);
        // Chỉ bỏ qua khi chắc chắn không có mục nào của nhóm; còn mục (kể cả nghi rỗng) thì vẫn chạy lệnh gỡ – thao tác an toàn, lặp lại được.
        if (!wantRules && GetMdmPolicy(out _) is null)
            return ApplyResult.Ok("AppLocker: không có quy tắc nào cần áp dụng.");

        var gpPolicy = wantRules ? GetLocalPolicy(out _) : null;
        var rules = wantRules
            ? BuildRules(children, blockedExes, strict, allowedFolders, includeAllowAll: !HasForeignExeRules(gpPolicy))
            : [];

        var file = Path.Combine(AppPaths.DataDir, "applocker-policy.xml");
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            if (rules.Count > 0) File.WriteAllText(file, BuildRuleCollection(rules), new UTF8Encoding(false));
            else if (File.Exists(file)) File.Delete(file);

            var script =
                $"$ns='{MdmNamespace}';$cls='{MdmClass}';$parent='{MdmParentId}';$file='{file.Replace("'", "''")}';" +
                $"Get-CimInstance -Namespace $ns -ClassName $cls -ErrorAction SilentlyContinue | Where-Object {{ $_.ParentID -like '{MdmParentPattern}' }} | Remove-CimInstance;" +
                "if (Test-Path $file) { $p=[Security.SecurityElement]::Escape([IO.File]::ReadAllText($file));" +
                "New-CimInstance -Namespace $ns -ClassName $cls -Property @{ParentID=$parent;InstanceID='EXE';Policy=$p} | Out-Null }";
            var (ok, output) = RunPowerShell(script, importAppLocker: false);
            if (!ok) return ApplyResult.Fail("Không ghi được chính sách AppLocker (CSP): " + output);
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }

        // Kiểm chứng bằng cách đọc lại nhóm CSP. Một số bản Windows không trả nội dung Policy khi đọc –
        // khi đó chỉ kiểm tra nhóm có tồn tại hay không.
        var readBack = GetMdmPolicy(out var readError);
        if (readError is not null) return ApplyResult.Fail("Đã ghi AppLocker nhưng không đọc lại được: " + readError);
        if (rules.Count == 0)
        {
            // MDM Bridge có thể còn giữ một mục rỗng của nhóm sau khi xóa (kiểm chứng trong Sandbox) – điều quan trọng là
            // không còn quy tắc nào của ta. Nếu còn, đọc lại/xóa lại vài lần trước khi kết luận.
            for (var attempt = 0; HasOurRules(readBack) && attempt < 8; attempt++)
            {
                Thread.Sleep(1500);
                if (attempt == 4)
                    RunPowerShell($"Get-CimInstance -Namespace '{MdmNamespace}' -ClassName {MdmClass} -ErrorAction SilentlyContinue | " +
                                  $"Where-Object {{ $_.ParentID -like '{MdmParentPattern}' }} | Remove-CimInstance", importAppLocker: false);
                readBack = GetMdmPolicy(out _);
            }
            return !HasOurRules(readBack)
                ? ApplyResult.Ok("Đã gỡ toàn bộ quy tắc AppLocker của Con Ngoan.")
                : ApplyResult.Fail($"Không gỡ được nhóm quy tắc AppLocker của Con Ngoan (còn {(readBack == MdmExistsWithoutContent ? "nhóm rỗng" : CountOurRules(readBack) + " quy tắc")}).");
        }
        if (readBack is null)
            return ApplyResult.Fail("Đã ghi AppLocker nhưng đọc lại không thấy nhóm quy tắc của Con Ngoan.");
        var count = readBack == MdmExistsWithoutContent ? rules.Count : CountOurRules(readBack);
        if (count != rules.Count)
        {
            var sample = readBack.Length > 400 ? readBack[..400] + "…" : readBack;
            return ApplyResult.Fail($"Đã ghi AppLocker nhưng đọc lại thấy {count}/{rules.Count} quy tắc. Nội dung đọc được: {sample}");
        }

        return EnsureAppIdService()
            ? ApplyResult.Ok($"AppLocker: đã áp dụng {count} quy tắc cho {children.Count} tài khoản con" + (strict ? " (bật chế độ nghiêm ngặt)." : "."), count)
            : ApplyResult.Fail("Đã ghi quy tắc AppLocker nhưng không khởi động được dịch vụ Application Identity (AppIDSvc) – quy tắc chưa có hiệu lực.");
    }

    /// <summary>
    /// Dự phòng khi gỡ cài đặt mà dịch vụ nền không chạy: chạy "ConNgoan.Service.exe --remove-applocker" một lần bằng
    /// quyền SYSTEM qua Task Scheduler (Administrator được phép làm việc này), rồi xóa tác vụ.
    /// </summary>
    public static ApplyResult RemoveViaSystemTask(string serviceExe)
    {
        const string task = "ConNgoan-AppLockerCleanup";
        var schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        try
        {
            var (created, createOut) = Run(schtasks,
                $"/Create /TN {task} /TR \"\\\"{serviceExe}\\\" --remove-applocker\" /SC ONCE /ST 00:00 /RU SYSTEM /RL HIGHEST /F");
            if (!created) return ApplyResult.Fail("Không tạo được tác vụ gỡ AppLocker: " + createOut);
            Run(schtasks, $"/Run /TN {task}");
            for (var i = 0; i < 60; i++)
            {
                Thread.Sleep(1000);
                var (_, query) = Run(schtasks, $"/Query /TN {task} /FO LIST /V");
                if (query.Contains("Last Result:                          0", StringComparison.Ordinal) ||
                    (i > 3 && !query.Contains("Running", StringComparison.OrdinalIgnoreCase)))
                    break;
            }
            return ApplyResult.Ok("Đã gỡ quy tắc AppLocker bằng tác vụ SYSTEM tạm thời.");
        }
        finally
        {
            Run(schtasks, $"/Delete /TN {task} /F");
        }
    }

    /// <summary>Mẫu so khớp ParentID khi đọc (CSP có thể trả về có hoặc không có tiền tố "./Vendor/MSFT").</summary>
    private const string MdmParentPattern = "*ApplicationLaunchRestrictions/" + MdmGrouping;

    /// <summary>Giá trị trả về khi nhóm CSP tồn tại nhưng Windows không trả nội dung Policy.</summary>
    public const string MdmExistsWithoutContent = "EXISTS";

    /// <summary>Chính sách đọc được có còn quy tắc của Con Ngoan không ("EXISTS" = không rõ nội dung → coi như còn).</summary>
    public static bool HasOurRules(string? mdmPolicy) =>
        mdmPolicy is not null && (mdmPolicy == MdmExistsWithoutContent || CountOurRules(mdmPolicy) > 0);

    /// <summary>
    /// Chính sách AppLocker CSP của nhóm Con Ngoan: null nếu chưa có, "EXISTS" nếu có nhưng không đọc được nội dung.
    /// Cần quyền SYSTEM.
    /// </summary>
    public static string? GetMdmPolicy(out string? error)
    {
        // Có thể có nhiều mục (MDM giữ lại mục rỗng sau khi xóa) → gộp nội dung của tất cả mục vào một phần tử gốc.
        var (ok, output) = RunPowerShell(
            $"$i = @(Get-CimInstance -Namespace '{MdmNamespace}' -ClassName {MdmClass} -ErrorAction SilentlyContinue | " +
            $"Where-Object {{ $_.ParentID -like '{MdmParentPattern}' }}); " +
            "$p = @($i | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Policy) } | ForEach-Object { " +
            "$x = $_.Policy; if ($x.TrimStart().StartsWith('&lt;')) { [Net.WebUtility]::HtmlDecode($x) } else { $x } }); " +
            $"$r = if ($p.Count -gt 0) {{ '<Groups>' + ($p -join '') + '</Groups>' }} elseif ($i.Count -gt 0) {{ '{MdmExistsWithoutContent}' }} else {{ '' }}; " +
            // Xuất Base64: Windows PowerShell ngắt dòng chuỗi dài theo độ rộng console, làm hỏng XML (kiểm chứng trong Sandbox).
            "[Console]::Out.Write([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($r)))",
            importAppLocker: false);
        error = ok ? null : output;
        if (!ok || string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(output.Trim()));
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }
        catch (FormatException)
        {
            error = "Dữ liệu đọc từ AppLocker CSP không hợp lệ.";
            return null;
        }
    }

    /// <summary>Chính sách Group Policy cục bộ (để biết tổ chức có quy tắc AppLocker riêng hay không).</summary>
    public static string? GetLocalPolicy(out string? error)
    {
        var (ok, output) = RunPowerShell("(Get-AppLockerPolicy -Local -Xml)");
        error = ok ? null : output;
        return ok ? output.Trim() : null;
    }

    /// <summary>AppLocker chỉ có hiệu lực khi dịch vụ Application Identity chạy. appidtel bật tự khởi động theo cách Microsoft hướng dẫn.</summary>
    public static bool EnsureAppIdService()
    {
        if (IsAppIdServiceRunning()) return true;
        Run(Path.Combine(Environment.SystemDirectory, "appidtel.exe"), "start");
        Run(Path.Combine(Environment.SystemDirectory, "sc.exe"), "start AppIDSvc");
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("AppIDSvc");
            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }

    public static bool IsAppIdServiceRunning()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("AppIDSvc");
            return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static (bool Ok, string Output) Run(string exe, string args, int timeoutMs = 60_000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            })!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (false, "Hết thời gian chờ.");
            }
            // Thành công: chỉ dùng stdout. Windows PowerShell ghi bản tin tiến trình (CLIXML) ra stderr khi bị chuyển hướng –
            // gộp vào sẽ làm hỏng dữ liệu (kiểm chứng trong Sandbox). Lỗi: trả cả hai để có thông tin chẩn đoán.
            return p.ExitCode == 0
                ? (true, stdout.Result.Trim())
                : (false, (stdout.Result + " " + stderr.Result).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Chạy lệnh Windows PowerShell. Đầu ra UTF-8.</summary>
    public static (bool Ok, string Output) RunPowerShell(string command, bool importAppLocker = true)
    {
        var script = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                     (importAppLocker ? "Import-Module AppLocker;" : "") + command;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return Run(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}");
    }
}
