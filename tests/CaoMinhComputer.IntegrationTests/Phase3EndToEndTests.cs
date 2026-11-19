using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security;
using System.Security.Principal;
using System.Text.Json;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>Test đầu-cuối Giai đoạn 3 trong Windows Sandbox (Enterprise → có AppLocker).</summary>
[Collection("E2E")]
public class Phase3EndToEndTests
{
    private const string InstallDir = @"C:\Program Files\ConNgoan";
    private const string ChildName = "cmcchild";
    private const int ErrorAccessDisabledByPolicy = 1260;
    private static readonly string TestDir = @"C:\CmcTest";

    private static ProtectionCoordinator Real()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    /// <summary>Chạy chương trình bằng tài khoản con. Trả về mã thoát, hoặc -1260 nếu bị chính sách chặn.</summary>
    private static int RunAsChild(string exe)
    {
        var password = new SecureString();
        foreach (var ch in Environment.GetEnvironmentVariable("CMC_CHILD_PW") ?? "") password.AppendChar(ch);
        var psi = new ProcessStartInfo(exe, "/user")
        {
            UserName = ChildName,
            Password = password,
            Domain = Environment.MachineName,
            LoadUserProfile = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = @"C:\Windows\System32",
            RedirectStandardOutput = true
        };
        try
        {
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDisabledByPolicy)
        {
            return -ErrorAccessDisabledByPolicy;
        }
    }

    private static int RunAsMe(string exe)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, "/user") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return p.ExitCode;
    }

    private static bool Eventually(Func<bool> condition, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(1000);
        }
        return condition();
    }

    [SandboxFact]
    public void AppLocker_blocks_listed_app_and_strict_mode_for_child_only_then_restores()
    {
        var c = Real();
        Assert.True(c.AppLockerAvailable, "Sandbox phải là bản Enterprise có AppLocker.");
        var whoami = Path.Combine(Environment.SystemDirectory, "whoami.exe");

        // Đăng nhập con một lần để tạo hồ sơ (cần cho DisallowRun/đường dẫn thư mục con).
        Assert.Equal(0, RunAsChild(whoami));

        Directory.CreateDirectory(TestDir);
        var game = Path.Combine(TestDir, "cmc-e2e-game.exe");
        var portable = Path.Combine(TestDir, "portable-tool.exe");
        File.Copy(whoami, game, true);
        File.Copy(whoami, portable, true);
        Assert.Equal(0, RunAsChild(portable)); // trước khi chặn: chạy được

        var child = LocalAccountService.GetAccounts().Single(a => a.Name == ChildName);
        c.Apps.Add("Game thử nghiệm", "cmc-e2e-game.exe", game, enabled: true);
        c.Settings.SetChildSids([child.Sid]);
        ApplyResult removed;
        try
        {
            // 1) Chặn theo danh sách ứng dụng (không bật nghiêm ngặt).
            c.Settings.SetBool(SettingKeys.AppLockerStrict, false);
            var result = c.ApplyAppBlocking();
            Assert.True(result.Success, result.Message);
            Assert.True(AppLockerManager.IsAppIdServiceRunning());
            Assert.True(Eventually(() => RunAsChild(game) == -ErrorAccessDisabledByPolicy), "AppLocker không chặn ứng dụng trong danh sách.");
            Assert.Equal(0, RunAsChild(portable)); // không nghiêm ngặt → file khác vẫn chạy
            Assert.Equal(0, RunAsMe(game));        // phụ huynh không bị chặn

            // 2) Chế độ nghiêm ngặt: chặn mọi file ngoài Windows/Program Files.
            c.Settings.SetBool(SettingKeys.AppLockerStrict, true);
            var strict = c.ApplyAppBlocking();
            Assert.True(strict.Success, strict.Message);
            Assert.Contains("3 quy tắc", strict.Message); // cho phép mặc định + chặn game + nghiêm ngặt
            Assert.True(Eventually(() => RunAsChild(portable) == -ErrorAccessDisabledByPolicy), "Chế độ nghiêm ngặt không chặn file portable.");
            var fresh = Path.Combine(TestDir, "fresh-download.exe");
            File.Copy(whoami, fresh, true);
            Assert.Equal(-ErrorAccessDisabledByPolicy, RunAsChild(fresh)); // file mới tải về
            Assert.Equal(0, RunAsChild(whoami)); // ứng dụng Windows vẫn chạy
            Assert.Equal(0, RunAsMe(portable));  // phụ huynh không bị chặn
        }
        finally
        {
            // Không assert trong finally để không che lỗi gốc của khối try.
            c.Settings.SetChildSids([]);
            c.Settings.SetBool(SettingKeys.AppLockerStrict, false);
            var rule = c.Apps.GetAll().Single(r => r.ProcessName == "cmc-e2e-game.exe");
            c.Apps.Delete(rule.Id);
            removed = c.ApplyAppBlocking();
        }

        // 3) Khôi phục: gỡ thành công và con chạy lại được mọi file.
        Assert.True(removed.Success, removed.Message);
        Assert.Contains("Đã gỡ toàn bộ quy tắc AppLocker", removed.Message);
        Assert.True(Eventually(() => RunAsChild(portable) == 0), "Sau khi gỡ, con vẫn bị chặn.");
        Assert.True(Eventually(() => RunAsChild(game) == 0), "Sau khi gỡ, ứng dụng trong danh sách vẫn bị chặn.");
    }

    [SandboxFact]
    public void Website_blocking_follows_active_session_owner()
    {
        var c = Real();
        var hosts = (HostsBlockingProvider)c.WebProvider;
        var me = WindowsIdentity.GetCurrent().User!.Value; // phiên Sandbox đang hoạt động
        var child = LocalAccountService.GetAccounts().Single(a => a.Name == ChildName);
        c.Blocks.Save(new Dictionary<string, bool> { ["facebook"] = true }, []);
        try
        {
            // Phụ huynh (phiên hiện tại) đang dùng máy, con là cmcchild → không chặn.
            c.Settings.Set(SettingKeys.WebScope, "children");
            c.Settings.SetChildSids([child.Sid]);
            var parentActive = c.ApplyWebBlocking();
            Assert.True(parentActive.Success, parentActive.Message);
            Assert.Contains("chưa chặn website", parentActive.Message);
            Assert.Empty(hosts.GetAppliedDomains());

            // Phiên hiện tại là của con → chặn.
            c.Settings.SetChildSids([me]);
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Contains("facebook.com", hosts.GetAppliedDomains());

            // "Mọi tài khoản" → chặn dù phụ huynh đang dùng máy.
            c.Settings.Set(SettingKeys.WebScope, "all");
            c.Settings.SetChildSids([child.Sid]);
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Contains("facebook.com", hosts.GetAppliedDomains());

            // Dịch vụ không coi việc đổi phạm vi là "HOSTS bị can thiệp".
            c.Settings.Set(SettingKeys.WebScope, "children");
            c.VerifyAndRepairWeb();
            Assert.Empty(hosts.GetAppliedDomains());
            Assert.Contains(c.Logs.Query(new LogQuery { Category = LogCategory.Website }), l => l.Message.StartsWith("Tạm mở chặn website"));
        }
        finally
        {
            c.Settings.SetChildSids([]);
            c.Settings.Delete(SettingKeys.WebScope);
            c.Blocks.Save(new Dictionary<string, bool> { ["facebook"] = false }, []);
            c.ApplyWebBlocking();
        }
    }

    [SandboxFact]
    public async Task Agent_reports_requests_extra_time_and_impostor_is_rejected()
    {
        var c = Real();
        var me = WindowsIdentity.GetCurrent().User!.Value;
        var agent = Path.Combine(InstallDir, AgentProtocol.AgentExeName);
        c.Settings.SaveTimeConfig(new TimeConfig { Enabled = true, TimeZoneId = TimeZoneInfo.Local.Id });
        c.Settings.SetChildSids([me]);
        try
        {
            Assert.Equal(0, RunAgent(agent, "--report-once"));

            // Tiến trình không phải Agent (chính test này) bị từ chối dù có quyền mở pipe.
            await using (var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut))
            {
                await pipe.ConnectAsync(3000);
                await PipeProtocol.WriteLineAsync(pipe, "{\"cmd\":\"report\",\"idleSeconds\":99999}", CancellationToken.None);
                var reply = JsonSerializer.Deserialize<AgentProtocol.Response>((await PipeProtocol.ReadLineAsync(pipe, CancellationToken.None))!, PipeProtocol.Json)!;
                Assert.False(reply.Ok);
                Assert.Contains("không được phép", reply.Error);
            }

            Assert.Equal(0, RunAgent(agent, "--request", "30", "Con", "làm", "nốt", "bài"));
            var pending = Assert.Single(c.Requests.GetPending(), r => r.Sid == me);
            Assert.Equal(30, pending.Minutes);
            Assert.Equal("Con làm nốt bài", pending.Reason);
            Assert.Equal(2, RunAgent(agent, "--request", "15")); // đang chờ duyệt → từ chối
            Assert.True(c.DecideExtraRequest(pending.Id, approve: true).Success);
        }
        finally
        {
            c.Settings.SetChildSids([]);
            c.Settings.SaveTimeConfig(new TimeConfig { Enabled = false, TimeZoneId = TimeZoneInfo.Local.Id });
        }
    }

    [SandboxFact]
    public async Task Idle_time_reported_by_agent_is_not_counted()
    {
        var c = Real();
        var me = WindowsIdentity.GetCurrent().User!.Value;
        var agentExe = Path.Combine(InstallDir, AgentProtocol.AgentExeName);
        var config = new TimeConfig { Enabled = false, TimeZoneId = TimeZoneInfo.Local.Id };
        c.Settings.SaveTimeConfig(config);
        c.Settings.Set(SettingKeys.IdleThresholdSeconds, "5");
        c.Settings.SetBool(SettingKeys.IdleExclusion, true);
        c.Settings.SetChildSids([me]);
        var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow));
        using var agent = Process.Start(agentExe)!; // Agent thật, báo idle 30 giây/lần
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8)); // Sandbox không có thao tác → idle ≥ 5 giây
            Assert.Equal(0, RunAgent(agentExe, "--report-once"));
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, Path.Combine(InstallDir, AgentProtocol.ServiceExeName));
            await Task.Delay(TimeSpan.FromSeconds(3));
            var before = c.Usage.Get(me, day).UsedSeconds;
            await Task.Delay(TimeSpan.FromSeconds(65));
            var idleCounted = c.Usage.Get(me, day).UsedSeconds - before;
            Assert.True(idleCounted <= 5,
                $"Thời gian không thao tác vẫn bị tính: {idleCounted} giây. Chẩn đoán: input idle {Diag.InputIdleSeconds()} s, " +
                $"ES_DISPLAY_REQUIRED={Diag.DisplayRequired()}, powercfg /requests: {Diag.PowerRequests()}");

            // Tắt tùy chọn → được tính lại.
            c.Settings.SetBool(SettingKeys.IdleExclusion, false);
            var before2 = c.Usage.Get(me, day).UsedSeconds;
            await Task.Delay(TimeSpan.FromSeconds(65));
            Assert.True(c.Usage.Get(me, day).UsedSeconds - before2 >= 20, "Tắt loại trừ idle nhưng thời gian không được tính.");
        }
        finally
        {
            try { agent.Kill(); } catch (InvalidOperationException) { }
            c.Settings.SetChildSids([]);
            c.Settings.Delete(SettingKeys.IdleThresholdSeconds);
            c.Settings.Delete(SettingKeys.IdleExclusion);
        }
    }

    private static int RunAgent(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(20_000);
        return p.ExitCode;
    }
}
