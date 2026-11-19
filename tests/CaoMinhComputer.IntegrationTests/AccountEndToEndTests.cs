using System.Diagnostics;
using System.Security;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>
/// Test đầu-cuối 1.4.0 trong Windows Sandbox: phụ huynh tạo tài khoản con ngay trong ứng dụng; tài khoản mới chưa có hồ sơ
/// nên chặn ứng dụng được ghi nhớ và dịch vụ tự áp dụng khi con đăng nhập lần đầu.
/// </summary>
[Collection("E2E")]
public class AccountEndToEndTests
{
    private const string KidName = "cmckid";
    private const string GameExe = "cmc-kid-game.exe";

    private static ProtectionCoordinator Real()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    private static bool Eventually(Func<bool> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(1000);
        }
        return condition();
    }

    private static int RunAs(string user, string password, string exe)
    {
        var secure = new SecureString();
        foreach (var ch in password) secure.AppendChar(ch);
        using var p = Process.Start(new ProcessStartInfo(exe, "/user")
        {
            UserName = user,
            Password = secure,
            Domain = Environment.MachineName,
            LoadUserProfile = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = @"C:\Windows\System32",
            RedirectStandardOutput = true
        })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        return p.ExitCode;
    }

    private static string RunNet(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("net.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return text;
    }

    private static List<string> DisallowRunValues(string sid) =>
        UserHive.With(sid, root =>
        {
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun");
            return key is null ? [] : key.GetValueNames().Select(n => key.GetValue(n)?.ToString() ?? "").ToList();
        });

    [SandboxFact]
    public async Task Child_account_created_in_app_gets_app_blocking_after_first_logon()
    {
        LocalAccountService.DeleteUser(KidName);
        var password = "Kid-" + Guid.NewGuid().ToString("N")[..12] + "!A1";

        // 1) Tạo tài khoản: Standard User, chưa có hồ sơ, không tạo trùng được.
        var created = LocalAccountService.CreateStandardUser(KidName, password, "Be Thu Nghiem", out var sid);
        Assert.True(created.Success, created.Message);
        Assert.NotNull(sid);

        var c = Real();
        try
        {
            var account = LocalAccountService.GetAccounts().Single(a => a.Name == KidName);
            Assert.Equal(sid, account.Sid);
            Assert.False(account.IsAdministrator);
            Assert.False(account.HasProfile);
            var netUser = RunNet($"user {KidName}");
            Assert.Contains("Be Thu Nghiem", netUser);
            Assert.Contains("*Users", netUser);
            Assert.DoesNotContain("*Administrators", netUser);
            var duplicate = LocalAccountService.CreateStandardUser(KidName, password, null, out _);
            Assert.False(duplicate.Success);
            Assert.Contains("Đã có tài khoản", duplicate.Message);

            // 2) Đánh dấu là con + chặn ứng dụng khi con chưa đăng nhập lần nào → không lỗi, ghi nhớ để áp dụng sau.
            c.Apps.Add("Game của bé", GameExe, null, enabled: true);
            c.Settings.SetChildSids([sid!]);
            var applied = c.ApplyAppBlocking();
            Assert.True(applied.Success, applied.Message);
            Assert.Contains("chưa đăng nhập lần nào", applied.Message);
            Assert.Contains(sid!, c.Settings.Get(SettingKeys.ProfilePending) ?? "");
            Assert.Null(c.Settings.Get(SettingKeys.DisallowRunPrefix + sid));

            // 3) Con đăng nhập lần đầu (Windows tạo hồ sơ) → dịch vụ tự áp dụng DisallowRun cho tài khoản này.
            Assert.Equal(0, RunAs(KidName, password, Path.Combine(Environment.SystemDirectory, "whoami.exe")));
            Assert.True(LocalAccountService.GetAccounts().Single(a => a.Name == KidName).HasProfile);
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, SandboxFactAttribute.InstalledService);
            Assert.True(Eventually(() => c.Settings.Get(SettingKeys.DisallowRunPrefix + sid) is not null),
                "Dịch vụ không áp dụng chặn ứng dụng sau khi con đăng nhập lần đầu.");
            Assert.True(Eventually(() => c.Settings.Get(SettingKeys.ProfilePending) is null), "Vẫn còn đánh dấu chờ hồ sơ.");
            Assert.Contains(GameExe, DisallowRunValues(sid!));
            Assert.Contains(c.Logs.Query(new LogQuery { Category = LogCategory.Application }),
                l => l.Message.Contains("đã đăng nhập lần đầu") && l.Message.Contains(KidName));
        }
        finally
        {
            c.Settings.SetChildSids([]);
            foreach (var rule in c.Apps.GetAll().Where(r => r.ProcessName == GameExe)) c.Apps.Delete(rule.Id);
            c.ApplyAppBlocking();
            LocalAccountService.DeleteUser(KidName);
        }

        Assert.DoesNotContain(LocalAccountService.GetAccounts(), a => a.Name == KidName);
    }
}
