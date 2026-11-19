using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>Test đầu-cuối Giai đoạn 2 – chỉ chạy trong Windows Sandbox sau khi cài bộ cài (CMC_E2E=1).</summary>
[Collection("E2E")]
public class TimeEndToEndTests
{
    private const string InstalledService = @"C:\Program Files\ConNgoan\ConNgoan.Service.exe";

    private static ProtectionCoordinator Real()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    [SandboxFact]
    public async Task Pipe_answers_status_and_refresh_from_genuine_service()
    {
        var ping = await PipeProtocol.SendAsync(PipeProtocol.CmdPing, InstalledService);
        Assert.NotNull(ping);
        Assert.True(ping!.Ok);

        var status = await PipeProtocol.SendAsync(PipeProtocol.CmdStatus, InstalledService);
        Assert.NotNull(status?.Sessions);
        var me = WindowsIdentity.GetCurrent().User!.Value;
        Assert.Contains(status!.Sessions!, s => s.Sid == me && s.IsActive);

        Assert.True((await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, InstalledService))!.Ok);

        // Kiểm tra chống giả mạo: nếu tiến trình server không phải exe mong đợi thì client từ chối.
        Assert.Null(await PipeProtocol.SendAsync(PipeProtocol.CmdPing, @"C:\Windows\notepad.exe"));
    }

    [SandboxFact]
    public async Task Pipe_rejects_unknown_command_and_oversized_message()
    {
        await using (var client = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut))
        {
            await client.ConnectAsync(3000);
            await PipeProtocol.WriteLineAsync(client, "{\"cmd\":\"format-disk\"}", CancellationToken.None);
            var reply = await PipeProtocol.ReadLineAsync(client, CancellationToken.None);
            Assert.Contains("\"ok\":false", reply);
        }

        await using (var client = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut))
        {
            await client.ConnectAsync(3000);
            var big = Encoding.UTF8.GetBytes(new string('x', 10_000));
            try
            {
                await client.WriteAsync(big);
                var reply = await PipeProtocol.ReadLineAsync(client, CancellationToken.None);
                Assert.True(string.IsNullOrEmpty(reply) || reply.Contains("\"ok\":false"));
            }
            catch (IOException)
            {
                // Server đóng kết nối – cũng là từ chối hợp lệ.
            }
        }

        // Dịch vụ vẫn hoạt động bình thường sau thông điệp xấu.
        Assert.True((await PipeProtocol.SendAsync(PipeProtocol.CmdPing, InstalledService))!.Ok);
    }

    [SandboxFact]
    public void Enumerates_current_session_as_active_and_unlocked()
    {
        var me = WindowsIdentity.GetCurrent().User!.Value;
        var session = WindowsSessions.Enumerate().Single(s => s.Sid == me);
        Assert.True(session.IsActive);
        Assert.False(session.IsLocked);
    }

    [SandboxFact]
    public async Task Service_switches_blocking_on_break_start_and_end()
    {
        var c = Real();
        var hosts = (HostsBlockingProvider)c.WebProvider;
        c.Blocks.Save(new Dictionary<string, bool> { ["facebook"] = true }, []);
        Assert.True(c.ApplyWebBlocking().Success);
        try
        {
            var config = c.Settings.GetTimeConfig();
            var local = DateTime.Now;
            config.TimeZoneId = TimeZoneInfo.Local.Id;
            config.CloseBrowsersAfterBreak = false;
            config.BreakWindows = [new TimeWindow { From = local.AddMinutes(-20).ToString("HH:mm"), To = local.AddMinutes(20).ToString("HH:mm") }];
            c.Settings.SaveTimeConfig(config);
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, InstalledService);
            Assert.True(await WaitFor(() => hosts.GetAppliedDomains().Count == 0, 60), "Dịch vụ không mở chặn khi vào giờ giải lao.");
            Assert.True(await WaitFor(() => c.Settings.GetBool(SettingKeys.BreakApplied), 60), "Dịch vụ thời gian không ghi nhận giờ giải lao.");

            config.BreakWindows = [];
            c.Settings.SaveTimeConfig(config);
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, InstalledService);
            Assert.True(await WaitFor(() => hosts.GetAppliedDomains().Contains("facebook.com"), 60), "Dịch vụ không chặn lại khi hết giờ giải lao.");
            Assert.True(await WaitFor(() => !c.Settings.GetBool(SettingKeys.BreakApplied), 60));
            // Nhật ký được ghi sau khi dịch vụ áp dụng xong cả web lẫn ứng dụng (AppLocker mất vài giây).
            bool Logged(string prefix) => c.Logs.Query(new LogQuery { Category = LogCategory.Time }).Any(l => l.Message.StartsWith(prefix));
            Assert.True(await WaitFor(() => Logged("Bắt đầu giờ giải lao"), 60), "Thiếu nhật ký bắt đầu giờ giải lao.");
            Assert.True(await WaitFor(() => Logged("Hết giờ giải lao"), 60), "Thiếu nhật ký hết giờ giải lao.");
        }
        finally
        {
            var config = c.Settings.GetTimeConfig();
            config.BreakWindows = [];
            c.Settings.SaveTimeConfig(config);
            c.Blocks.Save(new Dictionary<string, bool> { ["facebook"] = false }, []);
            c.ApplyWebBlocking();
        }
    }

    [SandboxFact]
    public async Task Service_counts_usage_of_child_session()
    {
        var c = Real();
        var me = WindowsIdentity.GetCurrent().User!.Value;
        var config = new TimeConfig { Enabled = false, TimeZoneId = TimeZoneInfo.Local.Id };
        c.Settings.SaveTimeConfig(config);
        c.Settings.SetChildSids([me]);
        try
        {
            var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow));
            var before = c.Usage.Get(me, day).UsedSeconds;
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, InstalledService);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, InstalledService); // nhịp đầu chỉ mở phiên
            Assert.True(await WaitFor(() => c.Usage.Get(me, day).UsedSeconds > before, 75), "Dịch vụ không cộng thời gian sử dụng.");
            Assert.Contains(c.Usage.RecentSessions(5), s => s.Sid == me && s.EndUtc is null);
        }
        finally
        {
            c.Settings.SetChildSids([]);
        }
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(500);
        }
        return condition();
    }
}

/// <summary>
/// Test cưỡng chế hết giờ: KHÓA (ngắt) phiên Sandbox hiện tại. Chạy riêng, sau cùng (CMC_E2E_LOCK=1).
/// </summary>
public sealed class LockFactAttribute : FactAttribute
{
    public LockFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CMC_E2E_LOCK") != "1")
            Skip = "Chỉ chạy ở bước cuối trong Windows Sandbox (CMC_E2E_LOCK=1).";
    }
}

public class TimeEnforcementEndToEndTests
{
    [LockFact]
    public async Task Service_warns_then_locks_child_session_when_time_is_up()
    {
        var db = new AppDatabase();
        db.Migrate();
        var c = new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
        var me = WindowsIdentity.GetCurrent().User!.Value;
        var config = new TimeConfig { Enabled = true, TimeZoneId = TimeZoneInfo.Local.Id, GraceMinutes = 1, Action = LimitAction.Lock };
        for (var i = 0; i < 7; i++) config.Days[i] = new DaySchedule { LimitMinutes = 30, From = "00:00", To = "00:00" };
        var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow));
        var startedAt = DateTime.UtcNow;
        try
        {
            c.Settings.SaveTimeConfig(config);
            // Còn 5 giây (tính cả thời gian được cấp thêm từ các test trước).
            c.Usage.SetUsed(me, day, 30 * 60 + c.Usage.Get(me, day).ExtraSeconds - 5);
            c.Settings.SetChildSids([me]);
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, @"C:\Program Files\ConNgoan\ConNgoan.Service.exe");

            var deadline = DateTime.UtcNow.AddMinutes(3);
            LogEntry? locked = null;
            while (DateTime.UtcNow < deadline && locked is null)
            {
                await Task.Delay(2000);
                locked = c.Logs.Query(new LogQuery { Category = LogCategory.Time })
                    .FirstOrDefault(l => l.TimestampUtc >= startedAt && l.Message.StartsWith("Đã khóa phiên"));
            }
            var logs = c.Logs.Query(new LogQuery { Category = LogCategory.Time }).Where(l => l.TimestampUtc >= startedAt).ToList();
            Assert.Contains(logs, l => l.Message.Contains("Sẽ khóa sau"));
            Assert.NotNull(locked);
        }
        finally
        {
            config.Enabled = false;
            c.Settings.SaveTimeConfig(config);
            c.Settings.SetChildSids([]);
        }
    }
}
