using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

public class TimeOfDayParserTests
{
    [Theory]
    [InlineData("11:30", 11, 30)]
    [InlineData("11h30", 11, 30)]
    [InlineData("7h", 7, 0)]
    [InlineData("7", 7, 0)]
    [InlineData(" 07:05 ", 7, 5)]
    [InlineData("21g15", 21, 15)]
    [InlineData("24:00", 24, 0)]
    public void Parses_vietnamese_formats(string text, int h, int m)
    {
        Assert.True(TimeOfDayParser.TryParse(text, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("24:30")]
    [InlineData("abc")]
    [InlineData("vd 11:30")]
    public void Rejects_invalid(string text) => Assert.False(TimeOfDayParser.TryParse(text, out _));

    [Fact]
    public void Normalizes() => Assert.Equal("07:05", TimeOfDayParser.Normalize("7h5"));
}

public class TimePolicyEngineTests
{
    // 2026-11-16 là Thứ Hai.
    private static readonly DateTime Monday = new(2026, 11, 16);

    private static TimeConfig Config(Action<TimeConfig>? change = null)
    {
        var c = new TimeConfig { Enabled = true, TimeZoneId = TimeZoneInfo.Utc.Id };
        for (var i = 0; i < 7; i++) c.Days[i] = new DaySchedule { Allowed = true, LimitMinutes = 120, From = "07:00", To = "21:00" };
        change?.Invoke(c);
        return c;
    }

    private static TimeStatus Eval(TimeConfig c, DateTime local, int usedMinutes = 0, int extraMinutes = 0, DateTime? paused = null) =>
        TimePolicyEngine.Evaluate(c, local, usedMinutes * 60, extraMinutes * 60, paused);

    [Fact]
    public void Disabled_is_always_allowed()
    {
        var s = Eval(Config(c => c.Enabled = false), Monday.AddHours(23), usedMinutes: 999);
        Assert.True(s.Allowed);
        Assert.Null(s.RemainingSeconds);
    }

    [Fact]
    public void Inside_hours_reports_remaining_from_limit()
    {
        var s = Eval(Config(), Monday.AddHours(10), usedMinutes: 30);
        Assert.True(s.Allowed);
        Assert.Equal(90 * 60, s.RemainingSeconds);
        Assert.Equal(120 * 60, s.LimitSeconds);
    }

    [Fact]
    public void Remaining_is_capped_by_end_of_allowed_window()
    {
        var s = Eval(Config(), Monday.AddHours(20).AddMinutes(30), usedMinutes: 0);
        Assert.Equal(30 * 60, s.RemainingSeconds);
    }

    [Fact]
    public void Remaining_is_capped_by_next_rest_window()
    {
        var c = Config(c => c.RestWindows.Add(new TimeWindow { From = "12:00", To = "13:30" }));
        var s = Eval(c, Monday.AddHours(11).AddMinutes(50));
        Assert.Equal(10 * 60, s.RemainingSeconds);
    }

    [Theory]
    [InlineData(6, 59, false, TimeBlockReason.OutsideHours)]
    [InlineData(7, 0, true, TimeBlockReason.None)]
    [InlineData(20, 59, true, TimeBlockReason.None)]
    [InlineData(21, 0, false, TimeBlockReason.OutsideHours)]
    public void Allowed_window_boundaries(int h, int m, bool allowed, TimeBlockReason reason)
    {
        var s = Eval(Config(), Monday.AddHours(h).AddMinutes(m));
        Assert.Equal(allowed, s.Allowed);
        Assert.Equal(reason, s.Reason);
    }

    [Fact]
    public void Limit_reached_blocks_and_extra_time_unblocks()
    {
        var c = Config();
        Assert.Equal(TimeBlockReason.LimitReached, Eval(c, Monday.AddHours(10), usedMinutes: 120).Reason);
        var withExtra = Eval(c, Monday.AddHours(10), usedMinutes: 120, extraMinutes: 15);
        Assert.True(withExtra.Allowed);
        Assert.Equal(15 * 60, withExtra.RemainingSeconds);
    }

    [Fact]
    public void Unlimited_minutes_only_follows_window()
    {
        var c = Config(c => c.Days[1].LimitMinutes = 0);
        var s = Eval(c, Monday.AddHours(10), usedMinutes: 600);
        Assert.True(s.Allowed);
        Assert.Equal(11 * 3600, s.RemainingSeconds);
        Assert.Null(s.LimitSeconds);
    }

    [Fact]
    public void Day_not_allowed_blocks_all_day()
    {
        var c = Config(c => c.Days[1].Allowed = false);
        Assert.Equal(TimeBlockReason.NotAllowedToday, Eval(c, Monday.AddHours(10)).Reason);
        Assert.True(Eval(c, Monday.AddDays(1).AddHours(10)).Allowed); // Thứ Ba vẫn được
    }

    [Fact]
    public void Rest_window_crossing_midnight()
    {
        var c = Config(c =>
        {
            c.Days[1].From = c.Days[1].To = "00:00"; // cả ngày
            c.RestWindows.Add(new TimeWindow { From = "22:00", To = "06:00" });
        });
        Assert.Equal(TimeBlockReason.RestTime, Eval(c, Monday.AddHours(23)).Reason);
        Assert.Equal(TimeBlockReason.RestTime, Eval(c, Monday.AddHours(5)).Reason);
        Assert.True(Eval(c, Monday.AddHours(6)).Allowed);
    }

    [Fact]
    public void Allowed_window_until_midnight_with_24h()
    {
        var c = Config(c => c.Days[1].To = "24:00");
        Assert.True(Eval(c, Monday.AddHours(23).AddMinutes(59)).Allowed);
        Assert.False(Eval(c, Monday.AddHours(6)).Allowed);
    }

    [Fact]
    public void Pause_overrides_everything_until_expiry()
    {
        var c = Config();
        var at = Monday.AddHours(23);
        var paused = Eval(c, at, usedMinutes: 500, paused: Monday.AddDays(1));
        Assert.True(paused.Allowed);
        Assert.True(paused.Paused);
        Assert.False(Eval(c, at, paused: Monday.AddHours(22)).Allowed);
    }

    [Fact]
    public void Break_windows()
    {
        var c = Config(c =>
        {
            c.BreakWindows.Add(new TimeWindow { From = "11:30", To = "12:59" });
            c.BreakWindows.Add(new TimeWindow { From = "23:00", To = "01:00" });
        });
        Assert.True(TimePolicyEngine.IsInBreak(c, Monday.AddHours(12), out var left));
        Assert.Equal(59 * 60, left);
        Assert.False(TimePolicyEngine.IsInBreak(c, Monday.AddHours(13)));
        Assert.True(TimePolicyEngine.IsInBreak(c, Monday.AddHours(0).AddMinutes(30)));
        Assert.False(TimePolicyEngine.IsInBreak(new TimeConfig(), Monday.AddHours(12)));
    }

    [Fact]
    public void Schedule_uses_configured_time_zone_not_machine_time_zone()
    {
        var c = Config(c => c.TimeZoneId = "SE Asia Standard Time"); // UTC+7, không có giờ mùa hè
        var local = TimePolicyEngine.ToScheduleTime(c, new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0), local);
        Assert.Equal("2026-11-16", TimePolicyEngine.DayKey(local));
    }

    [Fact]
    public void Validation_reports_vietnamese_errors()
    {
        var c = Config(c =>
        {
            c.Days[2].From = "25:00";
            c.RestWindows.Add(new TimeWindow { From = "10:00", To = "10:00" });
            for (var i = 0; i < 6; i++) c.BreakWindows.Add(new TimeWindow { From = $"{i + 8}:00", To = $"{i + 8}:30" });
        });
        var errors = TimePolicyEngine.Validate(c);
        Assert.Contains(errors, e => e.StartsWith("Thứ Ba"));
        Assert.Contains(errors, e => e.Contains("trùng nhau"));
        Assert.Contains(errors, e => e.Contains("tối đa 5"));
        Assert.Empty(TimePolicyEngine.Validate(Config()));
    }

    [Fact]
    public void Config_json_roundtrip_and_corrupt_fallback()
    {
        var c = Config(c =>
        {
            c.Action = LimitAction.Logoff;
            c.BreakWindows.Add(new TimeWindow { From = "11:30", To = "12:00" });
        });
        var back = TimeConfig.FromJson(c.ToJson());
        Assert.Equal(LimitAction.Logoff, back.Action);
        Assert.Single(back.BreakWindows);
        Assert.Equal(7, back.Days.Length);
        Assert.False(TimeConfig.FromJson("{ hỏng").Enabled);
        Assert.Equal(7, TimeConfig.FromJson("{\"Days\":[]}").Days.Length);
    }

    [Theory]
    [InlineData(30, "30 giây")]
    [InlineData(61, "2 phút")]
    [InlineData(3600, "1 giờ 0 phút")]
    public void Formats_duration(int seconds, string text) => Assert.Equal(text, TimePolicyEngine.FormatDuration(seconds));
}
