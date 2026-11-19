using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Data.Sqlite;

namespace CaoMinhComputer.IntegrationTests;

public class TimeIntegrationTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    private static (TempDir Dir, ProtectionCoordinator C, HostsBlockingProvider Hosts) Create()
    {
        var dir = new TempDir();
        var db = new AppDatabase(Path.Combine(dir.Path, "t.db"));
        db.Migrate();
        var hostsPath = Path.Combine(dir.Path, "hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\r\n");
        var hosts = new HostsBlockingProvider(hostsPath, Path.Combine(dir.Path, "bk"), flushDns: false);
        return (dir, new ProtectionCoordinator(db, hosts, appLockerEnabled: false), hosts);
    }

    [Fact]
    public void Usage_accumulates_and_extra_time_is_separate()
    {
        var (dir, c, _) = Create();
        using (dir)
        {
            c.Usage.AddUsed(Sid, "2026-11-16", 30);
            c.Usage.AddUsed(Sid, "2026-11-16", 45);
            c.Usage.AddExtra(Sid, "2026-11-16", 900);
            c.Usage.AddUsed(Sid, "2026-11-17", 10);
            var day = c.Usage.Get(Sid, "2026-11-16");
            Assert.Equal(75, day.UsedSeconds);
            Assert.Equal(900, day.ExtraSeconds);
            Assert.Equal(0, c.Usage.Get("S-1-5-21-other", "2026-11-16").UsedSeconds);
            Assert.Equal(2, c.Usage.GetRange("2026-11-10", "2026-11-17").Count);
        }
    }

    [Fact]
    public void Sessions_are_recorded_and_dangling_ones_closed()
    {
        var (dir, c, _) = Create();
        using (dir)
        {
            var start = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
            var id = c.Usage.StartSession(Sid, start);
            c.Usage.UpdateSession(id, 600, null);
            Assert.Null(c.Usage.RecentSessions(5).Single().EndUtc);

            Assert.Equal(1, c.Usage.CloseDanglingSessions());
            var s = c.Usage.RecentSessions(5).Single();
            Assert.Equal(start.AddSeconds(600), s.EndUtc);
            Assert.Equal("10 phút", s.DurationText);
        }
    }

    [Fact]
    public void Upgrades_version1_database_to_version2_without_losing_data()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "old.db");
        var db = new AppDatabase(path);
        db.Migrate();
        new AdminRepository(db).Create("phuhuynh", "pbkdf2-sha256$1000$AA==$AA==");
        using (var conn = db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE usage_daily; DROP TABLE usage_sessions; PRAGMA user_version=1;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var again = new AppDatabase(path);
        again.Migrate();
        Assert.Equal("phuhuynh", new AdminRepository(again).Get()!.Username);
        new UsageRepository(again).AddUsed(Sid, "2026-11-16", 5);
        Assert.Equal(5, new UsageRepository(again).Get(Sid, "2026-11-16").UsedSeconds);
    }

    [Fact]
    public void Time_status_uses_stored_usage_extra_time_and_pause()
    {
        var (dir, c, _) = Create();
        using (dir)
        {
            var config = new TimeConfig { Enabled = true, TimeZoneId = TimeZoneInfo.Utc.Id };
            for (var i = 0; i < 7; i++) config.Days[i] = new DaySchedule { LimitMinutes = 60, From = "00:00", To = "00:00" };
            c.Settings.SaveTimeConfig(config);
            var utc = DateTime.UtcNow;
            var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(config, utc));

            c.Usage.SetUsed(Sid, day, 3600);
            Assert.Equal(TimeBlockReason.LimitReached, c.GetTimeStatus(Sid, utc).Reason);

            c.GrantExtraTime(Sid, 15);
            var afterGrant = c.GetTimeStatus(Sid, utc);
            Assert.True(afterGrant.Allowed);
            Assert.InRange(afterGrant.RemainingSeconds!.Value, 1, 15 * 60);

            c.Usage.SetUsed(Sid, day, 99_999);
            Assert.False(c.GetTimeStatus(Sid, utc).Allowed);
            c.PauseTimeLimitsToday(true);
            Assert.True(c.GetTimeStatus(Sid, utc).Paused);
            c.PauseTimeLimitsToday(false);
            Assert.False(c.GetTimeStatus(Sid, utc).Allowed);
            Assert.Contains(c.Logs.Query(new LogQuery { Category = LogCategory.Admin }), l => l.Message.Contains("Cấp thêm 15 phút"));
        }
    }

    [Fact]
    public void Break_window_lifts_web_blocking_and_restores_after()
    {
        var (dir, c, hosts) = Create();
        using (dir)
        {
            c.Blocks.Save(new Dictionary<string, bool> { ["facebook"] = true }, []);
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Contains("facebook.com", hosts.GetAppliedDomains());

            var config = new TimeConfig { TimeZoneId = TimeZoneInfo.Utc.Id };
            var now = DateTime.UtcNow;
            config.BreakWindows.Add(new TimeWindow
            {
                From = now.AddMinutes(-30).ToString("HH:mm"),
                To = now.AddMinutes(30).ToString("HH:mm")
            });
            c.Settings.SaveTimeConfig(config);
            Assert.True(c.IsInBreakNow());
            var during = c.ApplyWebBlocking();
            Assert.True(during.Success);
            Assert.Contains("giờ giải lao", during.Message);
            Assert.Empty(hosts.GetAppliedDomains());

            config.BreakWindows.Clear();
            c.Settings.SaveTimeConfig(config);
            Assert.False(c.IsInBreakNow());
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Contains("facebook.com", hosts.GetAppliedDomains());
        }
    }
}
