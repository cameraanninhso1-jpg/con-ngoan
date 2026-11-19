using System.Diagnostics;
using System.Security;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Win32;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>
/// Test đầu-cuối 1.6.0 trong Windows Sandbox: chính sách trình duyệt + chống can thiệp CHỈ ghi vào tài khoản con,
/// gỡ sạch khi bỏ chọn; dọn chính sách cấp máy do bản 1.8.0 thử nghiệm để lại; hết giờ "mượn máy" thì dịch vụ tự bật lại.
/// </summary>
[Collection("E2E")]
public class SafetyEndToEndTests
{
    private const string Kid = "cmcsafe";
    private const string Chrome = @"Software\Policies\Google\Chrome";

    private static ProtectionCoordinator Real()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    private static bool Eventually(Func<bool> condition, int seconds = 40)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(1000);
        }
        return condition();
    }

    private static void FirstLogon(string user, string password)
    {
        var secure = new SecureString();
        foreach (var ch in password) secure.AppendChar(ch);
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "whoami.exe"))
        {
            UserName = user, Password = secure, Domain = Environment.MachineName, LoadUserProfile = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory, RedirectStandardOutput = true
        })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
    }

    private static object? KidValue(string sid, string path, string name) =>
        UserHive.With(sid, root =>
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name);
        });

    [SandboxFact]
    public async Task Browser_and_anti_tamper_policies_apply_to_child_only_and_resume_after_borrow()
    {
        LocalAccountService.DeleteUser(Kid);
        var password = "Kid-" + Guid.NewGuid().ToString("N")[..12] + "!A1";
        Assert.True(LocalAccountService.CreateStandardUser(Kid, password, null, out var sid).Success);
        FirstLogon(Kid, password);
        var c = Real();
        try
        {
            // Giả lập chính sách cấp máy do bản 1.8.0 để lại (áp cho cả phụ huynh) → phải được dọn.
            using (var hklm = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome"))
                hklm.SetValue("IncognitoModeAvailability", 1, RegistryValueKind.DWord);
            c.Changes.SaveOriginalIfAbsent(@"HKLM\SOFTWARE\Policies\Google\Chrome\IncognitoModeAvailability", false, null);

            c.Settings.SetBool(SettingKeys.SecurityAntiTamper, true);
            c.Settings.SetChildSids([sid!]);
            var applied = c.ApplyAppBlocking();
            Assert.True(applied.Success, applied.Message);

            // Tài khoản con có chính sách, máy (HKLM) và tài khoản phụ huynh thì không.
            Assert.Equal("off", KidValue(sid!, Chrome, "DnsOverHttpsMode"));
            Assert.Equal(1, KidValue(sid!, Chrome, "IncognitoModeAvailability"));
            Assert.Equal(1, KidValue(sid!, Chrome, "ForceGoogleSafeSearch"));
            Assert.Equal(1, KidValue(sid!, @"Software\Policies\Mozilla\Firefox", "DisablePrivateBrowsing"));
            Assert.Null(KidValue(sid!, Chrome + @"\ExtensionInstallBlocklist", "1")); // mặc định không chặn tiện ích
            Assert.Equal(1, KidValue(sid!, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"));
            Assert.Equal(2, KidValue(sid!, @"Software\Policies\Microsoft\Windows\System", "DisableCMD"));
            using (var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Google\Chrome"))
            {
                Assert.Null(hklm?.GetValue("IncognitoModeAvailability"));
                Assert.Null(hklm?.GetValue("DnsOverHttpsMode"));
            }
            using (var mine = Registry.CurrentUser.OpenSubKey(Chrome))
                Assert.Null(mine?.GetValue("DnsOverHttpsMode"));

            // Mượn máy: gỡ chính sách; hết hạn → dịch vụ tự bật lại (không cần Telegram).
            c.PauseProtectionFor(TimeSpan.FromMinutes(5), "Kiểm thử mượn máy");
            Assert.True(c.IsPaused);
            Assert.Null(KidValue(sid!, Chrome, "DnsOverHttpsMode"));
            Assert.Null(KidValue(sid!, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"));
            c.Settings.SetUtc(SettingKeys.ProtectionPausedUntilUtc, DateTime.UtcNow.AddSeconds(-5));
            await PipeProtocol.SendAsync(PipeProtocol.CmdRefresh, SandboxFactAttribute.InstalledService);
            Assert.True(Eventually(() => "off".Equals(KidValue(sid!, Chrome, "DnsOverHttpsMode"))),
                "Dịch vụ không tự bật lại chính sách khi hết giờ mượn máy.");
            Assert.Null(c.Settings.Get(SettingKeys.ProtectionPausedUntilUtc));
            Assert.Contains(c.Logs.Query(new LogQuery { Category = LogCategory.Admin }), l => l.Message.Contains("Hết thời gian mượn máy"));
            Assert.True(Eventually(() => KidValue(sid!, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr") is 1),
                "Chống can thiệp chưa được bật lại sau khi hết giờ mượn máy.");
            Thread.Sleep(5000); // chờ dịch vụ áp dụng xong AppLocker trước khi đổi cấu hình

            // Bỏ chọn tài khoản con → gỡ sạch.
            c.Settings.SetChildSids([]);
            c.ApplyAppBlocking();
            Assert.Null(KidValue(sid!, Chrome, "DnsOverHttpsMode"));
            Assert.Null(KidValue(sid!, Chrome, "IncognitoModeAvailability"));
            Assert.Null(KidValue(sid!, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"));
            Assert.Null(KidValue(sid!, @"Software\Policies\Microsoft\Windows\System", "DisableCMD"));
            Assert.Null(c.Settings.Get(SettingKeys.AntiTamperSids));
            Assert.Empty(c.BrowserPolicy.UsersWithChanges());
        }
        finally
        {
            c.Settings.SetChildSids([]);
            c.ResumeProtection();
            LocalAccountService.DeleteUser(Kid);
        }
    }
}
