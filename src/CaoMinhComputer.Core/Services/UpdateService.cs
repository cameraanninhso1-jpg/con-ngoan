using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Thông tin một bản phát hành (file version.json trên máy chủ). File này được ký bằng khóa bí mật của Cao Minh
/// (ECDSA P-256 / SHA-256, chữ ký nằm trong version.json.sig); app chỉ tin manifest có chữ ký hợp lệ.
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("published")] string? Published = null,
    [property: JsonPropertyName("notes")] string? Notes = null)
{
    public const string ProductName = "ConNgoan";

    [JsonIgnore]
    public Version ParsedVersion => System.Version.Parse(Version);
}

/// <summary>
/// Khóa công khai dùng kiểm tra bản cập nhật. Khóa bí mật tương ứng KHÔNG nằm trong mã nguồn
/// (do Cao Minh giữ riêng, ký bằng scripts\cmc-release.ps1). Có thể có nhiều khóa để đổi khóa dần.
/// </summary>
public static class UpdateKeys
{
    public static readonly string[] Trusted =
    [
        // cmc-update-2026 (ECDSA P-256, SubjectPublicKeyInfo, Base64)
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEc/W3L/ivkhmLApFFQyPReKVI92AjcdGdFIR9AgLdtvziupIVU4fv1AxML8B/mYnDkQjLyqd9mQ8vMpevPWgLOQ=="
    ];
}

/// <summary>Kiểm tra chữ ký manifest, phiên bản và file bộ cài đã tải.</summary>
public static class UpdateVerifier
{
    public const int MaxManifestBytes = 64 * 1024;
    public const long MaxInstallerBytes = 400L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };

    /// <summary>Ký nội dung manifest (dùng trong công cụ phát hành). Trả về chữ ký Base64 dạng IEEE P1363.</summary>
    public static string Sign(byte[] manifestBytes, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <summary>
    /// Xác minh chữ ký rồi đọc manifest. Trả về null kèm lý do nếu chữ ký sai, dữ liệu hỏng hoặc thiếu trường bắt buộc.
    /// </summary>
    public static UpdateManifest? Verify(byte[] manifestBytes, string? signatureBase64, out string? error) =>
        Verify(manifestBytes, signatureBase64, UpdateKeys.Trusted, out error);

    public static UpdateManifest? Verify(byte[] manifestBytes, string? signatureBase64, IEnumerable<string> trustedKeys, out string? error)
    {
        error = null;
        if (manifestBytes.Length == 0 || manifestBytes.Length > MaxManifestBytes) { error = "File thông tin phiên bản không hợp lệ."; return null; }
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String((signatureBase64 ?? "").Trim());
        }
        catch (FormatException)
        {
            error = "Chữ ký bản cập nhật không đúng định dạng.";
            return null;
        }

        var valid = false;
        foreach (var key in trustedKeys)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
                if (ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    valid = true;
                    break;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
            }
        }
        if (!valid) { error = "Chữ ký bản cập nhật KHÔNG hợp lệ – có thể file bị giả mạo, đã bỏ qua."; return null; }

        UpdateManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, Json);
        }
        catch (JsonException)
        {
            error = "File thông tin phiên bản bị hỏng.";
            return null;
        }
        if (m is null || m.Product != UpdateManifest.ProductName || !System.Version.TryParse(m.Version, out _) ||
            string.IsNullOrWhiteSpace(m.Url) || !Uri.TryCreate(m.Url, UriKind.Absolute, out _) ||
            m.Sha256 is not { Length: 64 } || !m.Sha256.All(Uri.IsHexDigit) || m.Size <= 0 || m.Size > MaxInstallerBytes)
        {
            error = "File thông tin phiên bản thiếu dữ liệu bắt buộc.";
            return null;
        }
        return m;
    }

    /// <summary>Chỉ chấp nhận bản mới hơn bản đang chạy (chống hạ cấp về bản cũ có lỗi).</summary>
    public static bool IsNewer(UpdateManifest manifest, Version current) => Normalize(manifest.ParsedVersion) > Normalize(current);

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>So khớp kích thước và SHA-256 của file bộ cài đã tải với manifest đã ký.</summary>
    public static bool VerifyFile(string path, UpdateManifest manifest, out string? error)
    {
        error = null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != manifest.Size)
        {
            error = "File bộ cài tải về sai kích thước.";
            return false;
        }
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            error = "File bộ cài tải về không khớp mã SHA-256 đã ký – có thể bị giả mạo, đã xóa.";
            return false;
        }
        return true;
    }

    public static string FormatVersion(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}

/// <summary>Tải manifest và bộ cài (HTTPS; file:// chỉ dùng khi kiểm thử với địa chỉ do quản trị đặt trong registry).</summary>
public static class UpdateFeed
{
    public const string DefaultUrl = "https://caominhcomputer.com/con-ngoan/version.json";
    public const string RegistryKey = @"SOFTWARE\ConNgoan";
    public const string RegistryValue = "UpdateUrl";

    /// <summary>Địa chỉ manifest: mặc định của Cao Minh, hoặc giá trị HKLM\SOFTWARE\ConNgoan\UpdateUrl (chỉ quản trị ghi được).</summary>
    public static string ManifestUrl
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
                if (key?.GetValue(RegistryValue) is string s && Uri.TryCreate(s, UriKind.Absolute, out _)) return s;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
            return DefaultUrl;
        }
    }

    public static string UpdatesDir => Path.Combine(AppPaths.DataDir, "updates");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ConNgoan-Updater/1.0");
        return client;
    }

    private static void CheckScheme(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp && !uri.IsFile)
            throw new InvalidOperationException("Địa chỉ cập nhật không được hỗ trợ: " + uri.Scheme);
    }

    private static async Task<byte[]> ReadSmallAsync(Uri uri, int maxBytes, CancellationToken ct)
    {
        CheckScheme(uri);
        if (uri.IsFile)
        {
            var info = new FileInfo(uri.LocalPath);
            if (info.Length > maxBytes) throw new InvalidDataException("File quá lớn.");
            return await File.ReadAllBytesAsync(uri.LocalPath, ct).ConfigureAwait(false);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("File quá lớn.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > maxBytes) throw new InvalidDataException("File quá lớn.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    public sealed record CheckResult(bool Ok, UpdateManifest? Manifest, bool IsNewer, string Message);

    /// <summary>Tải version.json + version.json.sig, kiểm tra chữ ký và so phiên bản.</summary>
    public static async Task<CheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        var url = ManifestUrl;
        try
        {
            var uri = new Uri(url);
            var manifestBytes = await ReadSmallAsync(uri, UpdateVerifier.MaxManifestBytes, ct).ConfigureAwait(false);
            var sigBytes = await ReadSmallAsync(new Uri(url + ".sig"), 4096, ct).ConfigureAwait(false);
            var manifest = UpdateVerifier.Verify(manifestBytes, System.Text.Encoding.ASCII.GetString(sigBytes), out var error);
            if (manifest is null) return new CheckResult(false, null, false, error!);
            var newer = UpdateVerifier.IsNewer(manifest, current);
            return new CheckResult(true, manifest, newer, newer
                ? $"Có phiên bản mới {manifest.Version}."
                : $"Bạn đang dùng phiên bản mới nhất ({UpdateVerifier.FormatVersion(current)}).");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            return new CheckResult(false, null, false, "Máy chủ Cao Minh chưa có thông tin phiên bản mới – bạn đang dùng bản mới nhất hiện có.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } code)
        {
            return new CheckResult(false, null, false, $"Máy chủ cập nhật đang lỗi (mã {(int)code}). Hãy thử lại sau.");
        }
        catch (HttpRequestException)
        {
            return new CheckResult(false, null, false, "Không kết nối được máy chủ cập nhật – kiểm tra kết nối Internet rồi thử lại.");
        }
        catch (OperationCanceledException)
        {
            return new CheckResult(false, null, false, "Máy chủ cập nhật phản hồi quá lâu – kiểm tra kết nối Internet rồi thử lại.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UriFormatException
                                       or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CheckResult(false, null, false, "Không kiểm tra được bản cập nhật: " + ex.Message);
        }
    }

    /// <summary>
    /// Tải bộ cài vào %ProgramData%\ConNgoan\updates (chỉ SYSTEM + Administrators ghi được),
    /// rồi kiểm tra kích thước + SHA-256 theo manifest đã ký. Trả về đường dẫn file hoặc null kèm lỗi.
    /// </summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(UpdateManifest manifest, CancellationToken ct = default)
    {
        Directory.CreateDirectory(UpdatesDir);
        foreach (var old in Directory.EnumerateFiles(UpdatesDir, "*.exe"))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var target = Path.Combine(UpdatesDir, $"ConNgoan-Setup-{manifest.Version}.exe");
        var temp = target + ".part";
        try
        {
            var uri = new Uri(manifest.Url);
            CheckScheme(uri);
            if (uri.IsFile)
            {
                File.Copy(uri.LocalPath, temp, overwrite: true);
            }
            else
            {
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } len && len != manifest.Size)
                    return (null, "Kích thước bộ cài trên máy chủ không khớp thông tin phiên bản.");
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > manifest.Size) return (null, "Bộ cài tải về lớn hơn thông tin phiên bản – đã hủy.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                }
            }

            if (!UpdateVerifier.VerifyFile(temp, manifest, out var error))
            {
                File.Delete(temp);
                return (null, error);
            }
            File.Move(temp, target, overwrite: true);
            return (target, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or InvalidOperationException
                                       or OperationCanceledException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            return (null, "Không tải được bộ cài: " + ex.Message);
        }
    }
}
