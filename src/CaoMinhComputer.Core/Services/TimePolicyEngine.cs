using System.Globalization;
using System.Text.RegularExpressions;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>Đọc giờ dạng "11:30", "11h30", "11h", "7" (phút 0).</summary>
public static partial class TimeOfDayParser
{
    [GeneratedRegex(@"^\s*(\d{1,2})\s*(?:[:hHgG.]\s*(\d{1,2})?)?\s*$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern().Match(text);
        if (!m.Success) return false;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (h is < 0 or > 24 || min is < 0 or > 59 || (h == 24 && min != 0)) return false;
        value = new TimeSpan(h, min, 0);
        return true;
    }

    /// <summary>Chuẩn hóa về "HH:mm" (24:00 giữ nguyên để biểu diễn hết ngày).</summary>
    public static string? Normalize(string? text) =>
        TryParse(text, out var t) ? (t.TotalHours >= 24 ? "24:00" : $"{t.Hours:00}:{t.Minutes:00}") : null;
}

/// <summary>
/// Logic quản lý thời gian dạng hàm thuần (không I/O) để kiểm thử đầy đủ:
/// lịch theo ngày, khung giờ, giờ nghỉ, giới hạn phút, thêm giờ, tạm dừng, giờ giải lao.
/// </summary>
public static class TimePolicyEngine
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <summary>Đổi giờ UTC sang giờ địa phương theo múi giờ phụ huynh đã cấu hình.</summary>
    public static DateTime ToScheduleTime(TimeConfig config, DateTime utcNow)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { tz = TimeZoneInfo.Local; }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
    }

    public static string DayKey(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Thời điểm t có nằm trong [from, to) không; hỗ trợ khung qua nửa đêm. from == to nghĩa là cả ngày.</summary>
    public static bool InWindow(TimeSpan t, TimeSpan from, TimeSpan to)
    {
        if (from == to) return true;
        return from < to ? t >= from && t < to : t >= from || t < to;
    }

    /// <summary>Số giây từ t tới mốc "at" kế tiếp (trong vòng 24 giờ).</summary>
    public static int SecondsUntil(TimeSpan t, TimeSpan at)
    {
        var delta = at - t;
        if (delta <= TimeSpan.Zero) delta += Day;
        return (int)Math.Ceiling(delta.TotalSeconds);
    }

    private static bool TryWindow(TimeWindow w, out TimeSpan from, out TimeSpan to)
    {
        to = default;
        return TimeOfDayParser.TryParse(w.From, out from) & TimeOfDayParser.TryParse(w.To, out to) && w.IsSet;
    }

    private static TimeSpan Clamp24(TimeSpan t) => t >= Day ? TimeSpan.Zero : t;

    /// <summary>Đang trong giờ giải lao không? Trả về số giây còn lại của khung giải lao (nếu có).</summary>
    public static bool IsInBreak(TimeConfig config, DateTime local, out int secondsLeft)
    {
        secondsLeft = 0;
        var t = local.TimeOfDay;
        foreach (var w in config.BreakWindows.Take(TimeConfig.MaxBreakWindows))
        {
            if (!TryWindow(w, out var from, out var to)) continue;
            from = Clamp24(from);
            to = Clamp24(to);
            if (from == to) continue; // khung không hợp lệ cho giải lao
            if (InWindow(t, from, to))
            {
                secondsLeft = SecondsUntil(t, to);
                return true;
            }
        }
        return false;
    }

    public static bool IsInBreak(TimeConfig config, DateTime local) => IsInBreak(config, local, out _);

    public static TimeStatus Evaluate(TimeConfig config, DateTime local, int usedSeconds, int extraSeconds, DateTime? pausedUntilLocal)
    {
        var day = config.Days[(int)local.DayOfWeek];
        int? limit = day.LimitMinutes > 0 ? day.LimitMinutes * 60 + Math.Max(0, extraSeconds) : null;

        if (!config.Enabled)
            return new TimeStatus(true, TimeBlockReason.None, null, usedSeconds, limit, false, "Chưa bật giới hạn thời gian.");

        if (pausedUntilLocal is { } paused && paused > local)
            return new TimeStatus(true, TimeBlockReason.None, null, usedSeconds, limit, true,
                $"Phụ huynh đã tạm dừng giới hạn đến {paused:HH:mm dd/MM}.");

        if (!day.Allowed)
            return Blocked(TimeBlockReason.NotAllowedToday, "Hôm nay không được dùng máy theo lịch.");

        var t = local.TimeOfDay;
        if (TimeOfDayParser.TryParse(day.From, out var from) && TimeOfDayParser.TryParse(day.To, out var to))
        {
            from = Clamp24(from);
            to = Clamp24(to);
            if (!InWindow(t, from, to))
                return Blocked(TimeBlockReason.OutsideHours, $"Ngoài giờ được dùng ({day.From}–{day.To}).");
        }
        else
        {
            from = to = TimeSpan.Zero;
        }

        foreach (var w in config.RestWindows.Take(TimeConfig.MaxRestWindows))
        {
            if (!TryWindow(w, out var rf, out var rt)) continue;
            rf = Clamp24(rf);
            rt = Clamp24(rt);
            if (rf != rt && InWindow(t, rf, rt))
                return Blocked(TimeBlockReason.RestTime, $"Đang là giờ nghỉ ({w.From}–{w.To}).");
        }

        if (limit is { } l && usedSeconds >= l)
            return Blocked(TimeBlockReason.LimitReached, $"Đã dùng hết {l / 60} phút của hôm nay.");

        // Thời gian còn lại = nhỏ nhất của: phút còn lại, tới hết khung giờ, tới giờ nghỉ kế tiếp.
        int? remaining = limit is { } lim ? lim - usedSeconds : null;
        if (from != to) remaining = Min(remaining, SecondsUntil(t, to));
        foreach (var w in config.RestWindows.Take(TimeConfig.MaxRestWindows))
        {
            if (!TryWindow(w, out var rf, out var rt) || Clamp24(rf) == Clamp24(rt)) continue;
            remaining = Min(remaining, SecondsUntil(t, Clamp24(rf)));
        }

        var description = remaining is { } r
            ? $"Còn {FormatDuration(r)}" + (limit is { } lm ? $" (đã dùng {usedSeconds / 60}/{lm / 60} phút)." : ".")
            : $"Không giới hạn số phút (đã dùng {usedSeconds / 60} phút).";
        return new TimeStatus(true, TimeBlockReason.None, remaining, usedSeconds, limit, false, description);

        TimeStatus Blocked(TimeBlockReason reason, string text) =>
            new(false, reason, 0, usedSeconds, limit, false, text);
    }

    private static int? Min(int? a, int b) => a is { } x ? Math.Min(x, b) : b;

    public static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{Math.Max(0, seconds)} giây";
        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return minutes >= 60 ? $"{minutes / 60} giờ {minutes % 60} phút" : $"{minutes} phút";
    }

    /// <summary>Kiểm tra cấu hình; trả về danh sách lỗi tiếng Việt (rỗng = hợp lệ).</summary>
    public static IReadOnlyList<string> Validate(TimeConfig config)
    {
        var errors = new List<string>();
        string[] names = ["Chủ nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];
        for (var i = 0; i < 7; i++)
        {
            var d = config.Days[i];
            if (!TimeOfDayParser.TryParse(d.From, out _) || !TimeOfDayParser.TryParse(d.To, out _))
                errors.Add($"{names[i]}: giờ không hợp lệ (dùng dạng 07:00 hoặc 7h).");
            if (d.LimitMinutes is < 0 or > 1440) errors.Add($"{names[i]}: số phút phải từ 0 đến 1440.");
        }
        CheckWindows(config.RestWindows, "Giờ nghỉ", TimeConfig.MaxRestWindows);
        CheckWindows(config.BreakWindows, "Giờ giải lao", TimeConfig.MaxBreakWindows);
        return errors;

        void CheckWindows(List<TimeWindow> windows, string label, int max)
        {
            if (windows.Count(w => w.IsSet) > max) errors.Add($"{label}: tối đa {max} khung.");
            foreach (var w in windows)
            {
                var hasFrom = !string.IsNullOrWhiteSpace(w.From);
                var hasTo = !string.IsNullOrWhiteSpace(w.To);
                if (!hasFrom && !hasTo) continue;
                if (hasFrom != hasTo) { errors.Add($"{label}: cần nhập cả giờ bắt đầu và kết thúc ({w.From}{w.To})."); continue; }
                if (!TimeOfDayParser.TryParse(w.From, out var f) || !TimeOfDayParser.TryParse(w.To, out var t))
                    errors.Add($"{label}: giờ không hợp lệ \"{w.From}–{w.To}\".");
                else if (Clamp24(f) == Clamp24(t))
                    errors.Add($"{label}: giờ bắt đầu và kết thúc trùng nhau ({w.From}).");
            }
        }
    }
}
