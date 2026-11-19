using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>
/// Test đầu-cuối sửa HOSTS/registry THẬT của máy. Chỉ chạy trong Windows Sandbox
/// (scripts\sandbox-test.ps1 đặt CMC_E2E=1 sau khi cài bộ cài). Bình thường tự bỏ qua.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    public const string InstalledService = @"C:\Program Files\ConNgoan\ConNgoan.Service.exe";

    public SandboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CMC_E2E") != "1")
            Skip = "Chỉ chạy trong Windows Sandbox (CMC_E2E=1).";
    }
}

[Collection("E2E")]
public class EndToEndTests
{
    private static ProtectionCoordinator RealCoordinator()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    [SandboxFact]
    public void Real_hosts_blocks_dns_resolution_and_restores()
    {
        const string domain = "example.com";
        var provider = new HostsBlockingProvider();
        var before = File.ReadAllText(provider.HostsPath);
        try
        {
            var result = provider.Apply([domain, "www." + domain]);
            Assert.True(result.Success, result.Message);

            // Sau khi chặn: Windows trả về 0.0.0.0/:: hoặc báo "không có dữ liệu" – không được ra IP thật.
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(domain);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Assert.Equal(System.Net.Sockets.SocketError.NoData, ex.SocketErrorCode);
                addresses = [];
            }
            Assert.All(addresses, a => Assert.True(a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any), a.ToString()));
        }
        finally
        {
            Assert.True(provider.RemoveAll().Success);
        }
        Assert.Equal(before.TrimEnd(), File.ReadAllText(provider.HostsPath).TrimEnd());
    }

    [SandboxFact]
    public void Installed_service_is_running_and_repairs_tampered_hosts()
    {
        using (var sc = new ServiceController(AppPaths.ServiceName))
            Assert.Equal(ServiceControllerStatus.Running, sc.Status);

        var c = RealCoordinator();
        c.Settings.SetBool(SettingKeys.ProtectionPaused, false);
        c.Blocks.Save(new Dictionary<string, bool> { ["tiktok"] = true }, ["cmc-e2e.example.com"]);
        try
        {
            Assert.True(c.ApplyWebBlocking().Success);
            var provider = (HostsBlockingProvider)c.WebProvider;

            // Mô phỏng người dùng xóa khối chặn.
            var tampered = HostsBlockingProvider.StripManagedBlock(File.ReadAllText(provider.HostsPath));
            File.WriteAllText(provider.HostsPath, tampered);
            Assert.Empty(provider.GetAppliedDomains());

            var expected = c.ComputeExpectedDomains();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!provider.IsInSync(expected) && DateTime.UtcNow < deadline) Thread.Sleep(500);
            Assert.True(provider.IsInSync(expected), "Dịch vụ không khôi phục HOSTS trong 30 giây.");
        }
        finally
        {
            c.Blocks.Save(new Dictionary<string, bool> { ["tiktok"] = false }, []);
            c.ApplyWebBlocking();
        }
    }

    [SandboxFact]
    public void Data_directory_is_not_accessible_to_standard_users()
    {
        var rules = new DirectoryInfo(AppPaths.DataDir).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        var allowed = rules.Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => r.IdentityReference.Value).ToHashSet();
        Assert.Contains("S-1-5-18", allowed);      // SYSTEM
        Assert.Contains("S-1-5-32-544", allowed);  // Administrators
        Assert.DoesNotContain("S-1-5-32-545", allowed); // Users
        Assert.DoesNotContain("S-1-1-0", allowed);      // Everyone
        Assert.DoesNotContain("S-1-5-11", allowed);     // Authenticated Users
    }

    [SandboxFact]
    public void Install_policy_changes_are_recorded_and_restored()
    {
        var c = RealCoordinator();
        var original = c.InstallPolicy.GetConsentPromptBehaviorUser();
        c.InstallPolicy.SetConsentPromptBehaviorUser(InstallPolicyService.ConsentAutoDeny);
        c.InstallPolicy.SetUserInstallsDisabled(true);
        Assert.Equal(InstallPolicyService.ConsentAutoDeny, c.InstallPolicy.GetConsentPromptBehaviorUser());
        Assert.True(c.InstallPolicy.IsUserInstallsDisabled());

        c.InstallPolicy.RestoreAll();
        Assert.Equal(original, c.InstallPolicy.GetConsentPromptBehaviorUser());
        Assert.False(c.InstallPolicy.IsUserInstallsDisabled());
    }

    /// <summary>Tài khoản "con" thử nghiệm do sandbox-test.ps1 tạo (Administrator, chưa đăng nhập lần nào).</summary>
    private const string ChildAccount = "cmcchild";

    [SandboxFact]
    public void Converts_child_account_to_standard_user_and_back()
    {
        var child = LocalAccountService.GetAccounts().Single(a => a.Name == ChildAccount);
        Assert.True(child.IsAdministrator);
        Assert.False(child.IsCurrentUser);
        Assert.True(WindowsEdition.Detect().Build >= 17763);

        var demote = LocalAccountService.MakeStandardUser(child.Sid);
        Assert.True(demote.Success, demote.Message);
        Assert.False(LocalAccountService.GetAccounts().Single(a => a.Name == ChildAccount).IsAdministrator);

        var promote = LocalAccountService.MakeAdministrator(child.Sid);
        Assert.True(promote.Success, promote.Message);
        Assert.True(LocalAccountService.GetAccounts().Single(a => a.Name == ChildAccount).IsAdministrator);
        LocalAccountService.MakeStandardUser(child.Sid);
    }

    [SandboxFact]
    public void DisallowRun_writes_and_removes_only_its_own_values()
    {
        // Dùng hive của tài khoản Sandbox hiện tại (đã nạp sẵn).
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        const string policy = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        using (var root = Microsoft.Win32.Registry.Users.OpenSubKey(sid, true)!)
        using (var list = root.CreateSubKey(policy + @"\DisallowRun"))
            list.SetValue("1", "foreign.exe"); // giá trị của "phần mềm khác"

        var state = DisallowRunProvider.Apply(sid, ["Game.exe", "explorer.exe", "Chat"], new DisallowRunState());
        Assert.Equal(["Game.exe", "Chat.exe"], state.Executables); // explorer.exe được bảo vệ
        using (var key = Microsoft.Win32.Registry.Users.OpenSubKey($@"{sid}\{policy}")!)
        {
            Assert.Equal(1, key.GetValue("DisallowRun"));
            using var list = key.OpenSubKey("DisallowRun")!;
            Assert.Equal("foreign.exe", list.GetValue("1"));
            Assert.Equal(["Chat.exe", "Game.exe", "foreign.exe"], list.GetValueNames().Select(n => (string)list.GetValue(n)!).Order(StringComparer.Ordinal));
        }

        var cleared = DisallowRunProvider.Apply(sid, [], state);
        Assert.Empty(cleared.OwnedValueNames);
        using (var key = Microsoft.Win32.Registry.Users.OpenSubKey($@"{sid}\{policy}")!)
        {
            Assert.Null(key.GetValue("DisallowRun"));
            using var list = key.OpenSubKey("DisallowRun")!;
            Assert.Equal(["1"], list.GetValueNames());
            Assert.Equal("foreign.exe", list.GetValue("1"));
        }
        using (var root = Microsoft.Win32.Registry.Users.OpenSubKey($@"{sid}\{policy}", true)!)
            root.DeleteSubKeyTree("DisallowRun");
    }

    [SandboxFact]
    public void App_blocking_is_deferred_for_account_without_profile()
    {
        // Tài khoản riêng cho test này để không phụ thuộc thứ tự chạy (tài khoản cmcchild có thể đã có hồ sơ).
        const string name = "cmcnoprofile";
        var password = "Np-" + Guid.NewGuid().ToString("N")[..8] + "!1"; // ≤ 14 ký tự để net user không hỏi xác nhận
        Assert.Equal(0, Net($"user {name} {password} /add"));
        var c = RealCoordinator();
        try
        {
            var child = LocalAccountService.GetAccounts().Single(a => a.Name == name);
            Assert.False(child.HasProfile);
            c.Apps.Add("E2E", "cmc-e2e-noprofile.exe", null, enabled: true);
            c.Settings.SetChildSids([child.Sid]);
            var result = c.ApplyAppBlocking();
            // Từ 1.4.0: không còn là lỗi – ghi nhớ và dịch vụ tự áp dụng khi tài khoản đăng nhập lần đầu.
            Assert.True(result.Success, result.Message);
            Assert.Contains("chưa đăng nhập lần nào", result.Message);
            Assert.Contains(child.Sid, c.Settings.Get(SettingKeys.ProfilePending) ?? "");
        }
        finally
        {
            var rule = c.Apps.GetAll().Single(r => r.ProcessName == "cmc-e2e-noprofile.exe");
            c.Apps.Delete(rule.Id);
            c.Settings.SetChildSids([]);
            Assert.True(c.ApplyAppBlocking().Success);
            Net($"user {name} /delete");
        }
    }

    private static int Net(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("net.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        })!;
        p.StandardInput.Close();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return p.ExitCode;
    }
}
