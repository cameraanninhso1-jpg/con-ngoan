namespace CaoMinhComputer.Core.Models;

/// <summary>Nhóm hiển thị của một nền tảng trên màn hình "Chặn web &amp; game".</summary>
public enum PlatformGroup
{
    Social,
    Game,
    WebGame,
    BypassTools,
    Entertainment
}

/// <summary>Định nghĩa một nền tảng có sẵn (Facebook, TikTok...).</summary>
/// <param name="Key">Khóa ổn định lưu trong database.</param>
/// <param name="Name">Tên hiển thị.</param>
/// <param name="Badge">Chữ viết tắt hiển thị trong biểu tượng tròn (không dùng logo thương hiệu).</param>
/// <param name="Color">Màu nền biểu tượng (#RRGGBB).</param>
/// <param name="Groups">Các nhóm mà nền tảng xuất hiện (Discord xuất hiện ở cả hai).</param>
/// <param name="Domains">Danh sách tên miền đầy đủ cần chặn (HOSTS không hỗ trợ wildcard).</param>
public sealed record PlatformDefinition(
    string Key,
    string Name,
    string Badge,
    string Color,
    IReadOnlyList<PlatformGroup> Groups,
    IReadOnlyList<string> Domains);

/// <summary>Kết quả của một thao tác áp dụng chính sách. Không bao giờ báo thành công khi chưa kiểm chứng.</summary>
public sealed record ApplyResult(bool Success, string Message, int Count = 0)
{
    public static ApplyResult Ok(string message, int count = 0) => new(true, message, count);
    public static ApplyResult Fail(string message) => new(false, message);
}

/// <summary>Một quy tắc chặn ứng dụng theo tên tiến trình.</summary>
public sealed class AppRule
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = "";
    /// <summary>Tên file thực thi, ví dụ "Discord.exe".</summary>
    public string ProcessName { get; set; } = "";
    public string? Path { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public enum LogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>Các loại sự kiện trong nhật ký (lưu trực tiếp bằng tiếng Việt để lọc).</summary>
public static class LogCategory
{
    public const string Website = "Website";
    public const string Application = "Ứng dụng";
    public const string Install = "Khóa cài đặt";
    public const string Admin = "Quản trị";
    public const string Settings = "Thay đổi cài đặt";
    public const string Service = "Dịch vụ";
    public const string System = "Hệ thống";
    public const string Time = "Thời gian sử dụng";

    public static readonly string[] All = [Website, Application, Time, Install, Admin, Settings, Service, System];
}

public sealed class LogEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";

    public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
    public string LevelText => Level switch
    {
        LogLevel.Warning => "Cảnh báo",
        LogLevel.Error => "Lỗi",
        _ => "Thông tin"
    };
}

/// <summary>Thông tin một tài khoản Windows cục bộ.</summary>
public sealed class LocalAccountInfo
{
    public string Name { get; init; } = "";
    public string Sid { get; init; } = "";
    public bool IsAdministrator { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsCurrentUser { get; init; }
    public bool HasProfile { get; init; }
}

/// <summary>Thông tin phiên bản Windows.</summary>
public sealed class WindowsEditionInfo
{
    public string ProductName { get; init; } = "";
    public string EditionId { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public int Build { get; init; }
    public bool IsWindows11 => Build >= 22000;
    public bool IsHome => EditionId.StartsWith("Core", StringComparison.OrdinalIgnoreCase);
    public bool IsPro => EditionId.StartsWith("Professional", StringComparison.OrdinalIgnoreCase);
    public bool IsEnterpriseOrEducation =>
        EditionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
        EditionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
        EditionId.StartsWith("ServerStandard", StringComparison.OrdinalIgnoreCase);

    public string FriendlyName =>
        $"{(IsWindows11 ? "Windows 11" : "Windows 10")} {EditionLabel} {DisplayVersion} (build {Build})".Replace("  ", " ").Trim();

    public string EditionLabel => IsHome ? "Home" : IsPro ? "Pro" : EditionId;

    /// <summary>Mô tả khả năng dùng AppLocker/WDAC theo edition .</summary>
    public string AppControlSupport => IsEnterpriseOrEducation
        ? "Hỗ trợ AppLocker chính thức: chặn ứng dụng theo tài khoản của con và chế độ nghiêm ngặt (bước 3)."
        : IsPro
            ? "Windows Pro: AppLocker áp dụng qua CSP (thường hoạt động) kết hợp mức cơ bản DisallowRun + dịch vụ đóng ứng dụng."
            : "Windows Home không có AppLocker/Group Policy. Hiện dùng mức cơ bản (DisallowRun + dịch vụ đóng ứng dụng).";
}
