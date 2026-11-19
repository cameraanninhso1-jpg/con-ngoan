using System.Text;
using System.Text.RegularExpressions;
using CaoMinhComputer.Core.Interfaces;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Chặn website bằng file HOSTS của Windows.
/// - Chỉ quản lý các dòng nằm giữa BeginMarker/EndMarker; không đụng tới nội dung của phần mềm khác.
/// - Sao lưu HOSTS trước mỗi lần ghi.
/// - Ghi đè nội dung tại chỗ (giữ nguyên ACL của file), có thử lại khi antivirus đang khóa file.
/// - Đọc lại file sau khi ghi để xác nhận; không báo thành công giả.
/// Giới hạn: không chặn VPN, DNS-over-HTTPS cấu hình riêng trong ứng dụng, IP trực tiếp, proxy.
/// </summary>
public sealed partial class HostsBlockingProvider : IWebsiteBlockingProvider
{
    public const string BeginMarker = "# >>> ConNgoan BEGIN - khoi do phan mem Con Ngoan quan ly, khong sua tay >>>";
    public const string EndMarker = "# <<< ConNgoan END <<<";
    private const int MaxBackups = 15;

    private readonly string _hostsPath;
    private readonly string _backupDir;
    private readonly bool _flushDns;

    public HostsBlockingProvider(string? hostsPath = null, string? backupDir = null, bool flushDns = true)
    {
        _hostsPath = hostsPath ?? DefaultHostsPath;
        _backupDir = backupDir ?? AppPaths.BackupDir;
        _flushDns = flushDns;
    }

    public static string DefaultHostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public string Name => "Windows HOSTS";
    public string HostsPath => _hostsPath;

    [GeneratedRegex(@"^\s*(0\.0\.0\.0|::)\s+\S+\s*$")]
    private static partial Regex ManagedEntryRegex();

    public ApplyResult Apply(IReadOnlyCollection<string> domains)
    {
        try
        {
            var (text, bom) = Read();
            var newText = BuildContent(text, domains, DateTime.Now);
            if (newText == text)
                return ApplyResult.Ok($"HOSTS đã đúng cấu hình ({domains.Count} tên miền).", domains.Count);

            Backup();
            Write(newText, bom);

            var applied = GetAppliedDomains();
            var expected = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
            if (!expected.SetEquals(applied))
                return ApplyResult.Fail("Đã ghi HOSTS nhưng khi đọc lại nội dung không khớp. Có thể phần mềm diệt virus đã chặn/khôi phục file HOSTS.");

            if (_flushDns) DnsCache.Flush();
            return domains.Count == 0
                ? ApplyResult.Ok("Đã gỡ toàn bộ chặn website khỏi HOSTS.")
                : ApplyResult.Ok($"Đã áp dụng chặn {domains.Count} tên miền qua HOSTS và làm mới DNS cache.", domains.Count);
        }
        catch (UnauthorizedAccessException)
        {
            return ApplyResult.Fail("Không có quyền ghi file HOSTS. Cần chạy với quyền Administrator, hoặc phần mềm diệt virus đang bảo vệ HOSTS.");
        }
        catch (IOException ex)
        {
            return ApplyResult.Fail("Không ghi được file HOSTS (file đang bị khóa): " + ex.Message);
        }
    }

    public ApplyResult RemoveAll() => Apply(Array.Empty<string>());

    public IReadOnlyList<string> GetAppliedDomains()
    {
        if (!File.Exists(_hostsPath)) return [];
        var (text, _) = Read();
        return ParseManagedDomains(text);
    }

    public bool IsInSync(IReadOnlyCollection<string> expectedDomains)
    {
        var applied = GetAppliedDomains();
        return new HashSet<string>(expectedDomains, StringComparer.OrdinalIgnoreCase).SetEquals(applied);
    }

    // ---------- Hàm thuần (pure) để kiểm thử ----------

    /// <summary>Tạo nội dung HOSTS mới: bỏ khối cũ của Con Ngoan, thêm khối mới (nếu có tên miền).</summary>
    public static string BuildContent(string original, IReadOnlyCollection<string> domains, DateTime now)
    {
        var newline = original.Contains("\r\n") || original.Length == 0 ? "\r\n" : "\n";
        var stripped = StripManagedBlock(original);
        if (domains.Count == 0) return stripped;

        var sb = new StringBuilder(stripped);
        if (sb.Length > 0 && !stripped.EndsWith('\n')) sb.Append(newline);
        sb.Append(BeginMarker).Append(newline);
        sb.Append("# Cap nhat: ").Append(now.ToString("yyyy-MM-dd HH:mm:ss")).Append(newline);
        foreach (var d in domains.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append("0.0.0.0 ").Append(d).Append(newline);
            sb.Append(":: ").Append(d).Append(newline);
        }
        sb.Append(EndMarker).Append(newline);
        return sb.ToString();
    }

    /// <summary>Bỏ mọi khối do Con Ngoan quản lý; nội dung ngoài khối giữ nguyên từng ký tự.</summary>
    public static string StripManagedBlock(string content)
    {
        var lines = SplitKeepEndings(content);
        var output = new StringBuilder(content.Length);
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Trim() == BeginMarker)
            {
                var end = -1;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].Trim() == BeginMarker) break;
                    if (lines[j].Trim() == EndMarker) { end = j; break; }
                }

                if (end >= 0)
                {
                    i = end + 1;
                }
                else
                {
                    // Khối hỏng (thiếu END): chỉ bỏ dòng BEGIN và các dòng đúng định dạng do ta tạo.
                    i++;
                    while (i < lines.Count &&
                           (ManagedEntryRegex().IsMatch(lines[i]) || lines[i].TrimStart().StartsWith("# Cap nhat:", StringComparison.Ordinal)))
                        i++;
                }
                continue;
            }
            output.Append(lines[i]);
            i++;
        }
        return output.ToString();
    }

    public static IReadOnlyList<string> ParseManagedDomains(string content)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var inBlock = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line == BeginMarker) { inBlock = true; continue; }
            if (line == EndMarker) { inBlock = false; continue; }
            if (!inBlock || line.StartsWith('#') || line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) result.Add(parts[1].ToLowerInvariant());
        }
        return result.ToList();
    }

    private static List<string> SplitKeepEndings(string content)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n') continue;
            list.Add(content.Substring(start, i - start + 1));
            start = i + 1;
        }
        if (start < content.Length) list.Add(content[start..]);
        return list;
    }

    // ---------- I/O ----------

    private (string Text, bool Bom) Read()
    {
        if (!File.Exists(_hostsPath)) return ("", false);
        var bytes = File.ReadAllBytes(_hostsPath);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        return (text, bom);
    }

    private void Write(string text, bool bom)
    {
        var bytes = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
        var attributes = File.Exists(_hostsPath) ? File.GetAttributes(_hostsPath) : FileAttributes.Normal;
        var wasReadOnly = attributes.HasFlag(FileAttributes.ReadOnly);
        if (wasReadOnly) File.SetAttributes(_hostsPath, attributes & ~FileAttributes.ReadOnly);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // FileMode.Create trên file có sẵn = cắt ngắn và ghi lại, giữ nguyên ACL gốc của HOSTS.
                    using var fs = new FileStream(_hostsPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    fs.Write(bytes);
                    fs.Flush(true);
                    break;
                }
                catch (IOException) when (attempt < 6)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }
        finally
        {
            if (wasReadOnly) File.SetAttributes(_hostsPath, attributes);
        }
    }

    private void Backup()
    {
        if (!File.Exists(_hostsPath)) return;
        Directory.CreateDirectory(_backupDir);
        var original = Path.Combine(_backupDir, "hosts-original.bak");
        if (!File.Exists(original))
        {
            File.Copy(_hostsPath, original);
            ClearReadOnly(original);
        }

        var target = Path.Combine(_backupDir, $"hosts-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak");
        File.Copy(_hostsPath, target, overwrite: true);
        // HOSTS thường được đặt Read-only; File.Copy giữ thuộc tính đó nên phải bỏ để còn xoay vòng/xóa được bản sao lưu.
        ClearReadOnly(target);

        foreach (var old in new DirectoryInfo(_backupDir).GetFiles("hosts-2*.bak")
                     .OrderByDescending(f => f.Name).Skip(MaxBackups))
        {
            try
            {
                old.Attributes &= ~FileAttributes.ReadOnly;
                old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Không xóa được bản sao lưu cũ không phải lỗi áp dụng chặn.
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
