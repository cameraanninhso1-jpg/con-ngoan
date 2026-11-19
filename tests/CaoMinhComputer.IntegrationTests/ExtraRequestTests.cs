using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.IntegrationTests;

public class ExtraRequestTests
{
    private const string Child = "S-1-5-21-9-9-9-1001";

    private static (TempDir Dir, ProtectionCoordinator C) Create()
    {
        var dir = new TempDir();
        var db = new AppDatabase(Path.Combine(dir.Path, "r.db"));
        db.Migrate();
        var hosts = new HostsBlockingProvider(Path.Combine(dir.Path, "hosts"), Path.Combine(dir.Path, "bk"), flushDns: false);
        var c = new ProtectionCoordinator(db, hosts, appLockerEnabled: false);
        c.Settings.SaveTimeConfig(new TimeConfig { Enabled = true, TimeZoneId = TimeZoneInfo.Utc.Id });
        return (dir, c);
    }

    [Fact]
    public void Child_request_is_validated_and_rate_limited()
    {
        var (dir, c) = Create();
        using (dir)
        {
            Assert.False(c.SubmitExtraRequest(Child, 30, "chưa là con").Success); // chưa đánh dấu tài khoản con
            c.Settings.SetChildSids([Child]);
            Assert.False(c.SubmitExtraRequest(Child, 999, null).Success);       // chỉ 15/30/60

            var ok = c.SubmitExtraRequest(Child, 30, "Làm nốt bài\u0007 tập");
            Assert.True(ok.Success, ok.Message);
            var pending = Assert.Single(c.Requests.GetPending());
            Assert.Equal("Làm nốt bài tập", pending.Reason);              // bỏ ký tự điều khiển
            Assert.False(c.SubmitExtraRequest(Child, 15, null).Success);   // đang có yêu cầu chờ

            // Duyệt → cộng giờ, không duyệt lại được lần hai.
            Assert.True(c.DecideExtraRequest(pending.Id, approve: true).Success);
            Assert.False(c.DecideExtraRequest(pending.Id, approve: false).Success);
            var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(c.Settings.GetTimeConfig(), DateTime.UtcNow));
            Assert.Equal(30 * 60, c.Usage.Get(Child, day).ExtraSeconds);
            Assert.Equal(ExtraRequestRepository.Approved, c.Requests.Latest(Child)!.Status);

            // Tối đa 5 yêu cầu/ngày.
            for (var i = 0; i < 4; i++)
            {
                Assert.True(c.SubmitExtraRequest(Child, 15, null).Success);
                Assert.True(c.DecideExtraRequest(c.Requests.Latest(Child)!.Id, approve: false).Success);
            }
            var limited = c.SubmitExtraRequest(Child, 15, null);
            Assert.False(limited.Success);
            Assert.Contains("5 lần", limited.Message);
            Assert.Contains(c.Logs.Query(new LogQuery { Category = LogCategory.Time }), l => l.Message.Contains("xin thêm 30 phút"));
        }
    }

    [Fact]
    public void Long_reason_is_truncated()
    {
        var (dir, c) = Create();
        using (dir)
        {
            c.Settings.SetChildSids([Child]);
            Assert.True(c.SubmitExtraRequest(Child, 15, new string('a', 500)).Success);
            Assert.Equal(ExtraRequestRepository.MaxReasonLength, c.Requests.GetPending().Single().Reason.Length);
        }
    }

    [Fact]
    public void Idle_settings_have_safe_defaults()
    {
        var (dir, c) = Create();
        using (dir)
        {
            Assert.True(c.IdleExclusionEnabled);
            Assert.Equal(300, c.IdleThresholdSeconds);
            c.Settings.Set(SettingKeys.IdleThresholdSeconds, "1");
            Assert.Equal(5, c.IdleThresholdSeconds); // giới hạn dưới
        }
    }
}
