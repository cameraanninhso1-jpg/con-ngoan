using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CaoMinhComputer.Core.Services;

/// <summary>Chuẩn hóa tên miền do phụ huynh nhập (bỏ http://, đường dẫn, cổng; chuyển IDN sang punycode).</summary>
public static partial class DomainNormalizer
{
    private static readonly IdnMapping Idn = new();

    [GeneratedRegex("^[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9])?$")]
    private static partial Regex LabelRegex();

    public static bool TryNormalize(string? input, out string domain, out string? note)
    {
        domain = "";
        note = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim().ToLowerInvariant();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        var cut = s.IndexOfAny(['/', '?', '#', '\\']);
        if (cut >= 0)
        {
            var rest = s[cut..];
            s = s[..cut];
            if (rest.Trim('/', ' ').Length > 0)
                note = $"Đã bỏ phần \"{rest}\": HOSTS chỉ chặn được toàn bộ tên miền, không chặn riêng đường dẫn.";
        }

        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];

        var colon = s.LastIndexOf(':');
        if (colon >= 0) s = s[..colon];

        if (s.StartsWith("*.", StringComparison.Ordinal))
        {
            s = s[2..];
            note = "HOSTS không hỗ trợ ký tự đại diện (*); đã chặn tên miền gốc và www/m.";
        }

        s = s.Trim().Trim('.');
        if (s.Length == 0) return false;

        if (IPAddress.TryParse(s, out _))
        {
            note = "Không thể chặn địa chỉ IP bằng HOSTS.";
            return false;
        }

        try
        {
            s = Idn.GetAscii(s);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (s.Length > 253 || !s.Contains('.') || s == "localhost") return false;

        var labels = s.Split('.');
        if (labels.Any(l => !LabelRegex().IsMatch(l))) return false;
        if (labels[^1].All(char.IsDigit)) return false;

        domain = s;
        return true;
    }

    /// <summary>
    /// Sinh các biến thể phổ biến cho tên miền do người dùng nhập: bản gốc, www. và m.
    /// Không thể biết hết subdomain của một dịch vụ – đây là giới hạn của HOSTS.
    /// </summary>
    public static IEnumerable<string> ExpandVariants(string domain)
    {
        yield return domain;
        if (domain.StartsWith("www.", StringComparison.Ordinal))
        {
            var bare = domain[4..];
            if (bare.Contains('.')) yield return bare;
            yield break;
        }
        if (domain.StartsWith("m.", StringComparison.Ordinal)) yield break;
        yield return "www." + domain;
        yield return "m." + domain;
    }
}
