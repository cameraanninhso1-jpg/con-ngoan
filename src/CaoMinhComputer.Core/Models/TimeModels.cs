using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaoMinhComputer.Core.Models;

/// <summary>Lịch của một ngày trong tuần.</summary>
public sealed class DaySchedule
{
    /// <summary>false = không được dùng máy cả ngày.</summary>
    public bool Allowed { get; set; } = true;

    /// <summary>Tổng số phút được dùng trong ngày; 0 = không giới hạn số phút.</summary>
    public int LimitMinutes { get; set; }

    /// <summary>Giờ bắt đầu được dùng (HH:mm). Từ == Đến nghĩa là cả ngày.</summary>
    public string From { get; set; } = "07:00";

    public string To { get; set; } = "21:00";
}

/// <summary>Một khung giờ trong ngày (có thể qua nửa đêm, ví dụ 22:00–06:00).</summary>
public sealed class TimeWindow
{
    public string? From { get; set; }
    public string? To { get; set; }

    [JsonIgnore]
    public bool IsSet => !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To);
}

public enum LimitAction
{
    /// <summary>Ngắt phiên về màn hình đăng nhập – ứng dụng của con vẫn mở, không mất dữ liệu.</summary>
    Lock,

    /// <summary>Đăng xuất – có thể mất dữ liệu chưa lưu.</summary>
    Logoff
}

/// <summary>Cấu hình quản lý thời gian (áp dụng cho mọi tài khoản của con).</summary>
public sealed class TimeConfig
{
    public const int MaxRestWindows = 3;
    public const int MaxBreakWindows = 5;

    public bool Enabled { get; set; }

    /// <summary>7 phần tử, chỉ số theo <see cref="DayOfWeek"/> (0 = Chủ nhật).</summary>
    public DaySchedule[] Days { get; set; } = DefaultDays();

    /// <summary>Giờ nghỉ – không được dùng máy (tối đa 3).</summary>
    public List<TimeWindow> RestWindows { get; set; } = [];

    /// <summary>Giờ giải lao – tạm mở chặn mạng xã hội/game (tối đa 5).</summary>
    public List<TimeWindow> BreakWindows { get; set; } = [];

    public bool CloseBrowsersAfterBreak { get; set; } = true;
    public LimitAction Action { get; set; } = LimitAction.Lock;

    /// <summary>Thời gian ân hạn sau khi hết giờ, trước khi khóa (phút).</summary>
    public int GraceMinutes { get; set; } = 2;

    /// <summary>Múi giờ phụ huynh cấu hình. Con đổi múi giờ máy cũng không làm lệch lịch.</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    public static DaySchedule[] DefaultDays() =>
    [
        new() { LimitMinutes = 180, From = "08:00", To = "21:30" }, // Chủ nhật
        new() { LimitMinutes = 120 },
        new() { LimitMinutes = 120 },
        new() { LimitMinutes = 120 },
        new() { LimitMinutes = 120 },
        new() { LimitMinutes = 120 },
        new() { LimitMinutes = 180, From = "08:00", To = "21:30" }  // Thứ bảy
    ];

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static TimeConfig FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TimeConfig();
        try
        {
            var config = JsonSerializer.Deserialize<TimeConfig>(json, Json) ?? new TimeConfig();
            if (config.Days is not { Length: 7 }) config.Days = DefaultDays();
            config.RestWindows ??= [];
            config.BreakWindows ??= [];
            config.GraceMinutes = Math.Clamp(config.GraceMinutes, 0, 15);
            return config;
        }
        catch (JsonException)
        {
            return new TimeConfig();
        }
    }
}

public enum TimeBlockReason
{
    None,
    NotAllowedToday,
    OutsideHours,
    RestTime,
    LimitReached
}

/// <summary>Kết quả đánh giá thời gian của một tài khoản con tại một thời điểm.</summary>
public sealed record TimeStatus(
    bool Allowed,
    TimeBlockReason Reason,
    int? RemainingSeconds,
    int UsedSeconds,
    int? LimitSeconds,
    bool Paused,
    string Description);

/// <summary>Một phiên Windows đang tồn tại trên máy.</summary>
public sealed record UserSession(int SessionId, string UserName, string? Sid, bool IsActive, bool IsLocked);

/// <summary>Thống kê sử dụng một ngày.</summary>
public sealed record DailyUsage(string Sid, string Day, int UsedSeconds, int ExtraSeconds);

/// <summary>Một phiên sử dụng đã ghi nhận.</summary>
public sealed class UsageSession
{
    public long Id { get; set; }
    public string Sid { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int Seconds { get; set; }

    public string AccountName { get; set; } = "";
    public DateTime StartLocal => StartUtc.ToLocalTime();
    public string EndText => EndUtc is { } e ? e.ToLocalTime().ToString("HH:mm") : "đang dùng";
    public string DurationText => Seconds >= 3600 ? $"{Seconds / 3600} giờ {Seconds % 3600 / 60} phút" : $"{Seconds / 60} phút";
}
