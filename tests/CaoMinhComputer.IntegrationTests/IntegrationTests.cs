using System.Text;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Data.Sqlite;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>Kiểm thử với file thật trong thư mục tạm (không đụng tới HOSTS của hệ thống).</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cmc-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

public class HostsProviderFileTests
{
    private const string Original = "# hosts goc\r\n127.0.0.1 localhost\r\n10.1.1.1 phan-mem-khac.local\r\n";

    [Fact]
    public void Apply_verify_and_remove_on_real_file()
    {
        using var dir = new TempDir();
        var hosts = Path.Combine(dir.Path, "hosts");
        File.WriteAllText(hosts, Original, new UTF8Encoding(false));
        var provider = new HostsBlockingProvider(hosts, Path.Combine(dir.Path, "backups"), flushDns: false);

        var result = provider.Apply(["facebook.com", "www.facebook.com"]);
        Assert.True(result.Success, result.Message);
        Assert.Equal(["facebook.com", "www.facebook.com"], provider.GetAppliedDomains());
        Assert.True(provider.IsInSync(["www.facebook.com", "facebook.com"]));
        Assert.False(provider.IsInSync(["facebook.com"]));
        Assert.StartsWith(Original, File.ReadAllText(hosts));

        // Sao lưu được tạo trước khi sửa, bản gốc giữ nguyên nội dung ban đầu.
        Assert.Equal(Original, File.ReadAllText(Path.Combine(dir.Path, "backups", "hosts-original.bak")));

        var removed = provider.RemoveAll();
        Assert.True(removed.Success, removed.Message);
        Assert.Equal(Original, File.ReadAllText(hosts));
        Assert.Empty(provider.GetAppliedDomains());
    }

    [Fact]
    public void Preserves_bom_and_readonly_attribute()
    {
        using var dir = new TempDir();
        var hosts = Path.Combine(dir.Path, "hosts");
        File.WriteAllText(hosts, Original, new UTF8Encoding(true));
        File.SetAttributes(hosts, FileAttributes.ReadOnly);
        var provider = new HostsBlockingProvider(hosts, Path.Combine(dir.Path, "b"), flushDns: false);

        Assert.True(provider.Apply(["x.com"]).Success);
        var bytes = File.ReadAllBytes(hosts);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.True(File.GetAttributes(hosts).HasFlag(FileAttributes.ReadOnly));

        // Bản sao lưu không được giữ Read-only (nếu không sẽ không xoay vòng/xóa được).
        Assert.All(Directory.GetFiles(Path.Combine(dir.Path, "b")),
            f => Assert.False(File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly), f));

        // Áp dụng nhiều lần (vượt số bản sao lưu tối đa) vẫn phải thành công.
        for (var i = 0; i < 20; i++) Assert.True(provider.Apply(["x.com", $"y{i}.com"]).Success);
        File.SetAttributes(hosts, FileAttributes.Normal);
    }

    [Fact]
    public void Repeated_apply_is_idempotent()
    {
        using var dir = new TempDir();
        var hosts = Path.Combine(dir.Path, "hosts");
        File.WriteAllText(hosts, Original);
        var provider = new HostsBlockingProvider(hosts, Path.Combine(dir.Path, "b"), flushDns: false);
        provider.Apply(["a.com"]);
        var first = File.ReadAllText(hosts);
        var second = provider.Apply(["a.com"]);
        Assert.True(second.Success);
        Assert.Equal(first, File.ReadAllText(hosts));
    }
}

public class DatabaseTests
{
    private static (TempDir Dir, AppDatabase Db) Create()
    {
        var dir = new TempDir();
        var db = new AppDatabase(Path.Combine(dir.Path, "test.db"));
        db.Migrate();
        return (dir, db);
    }

    [Fact]
    public void Migration_is_idempotent_and_seeds_sample_apps()
    {
        var (dir, db) = Create();
        using (dir)
        {
            db.Migrate();
            Assert.True(db.CheckIntegrity(out _));
            var apps = new AppRuleRepository(db).GetAll();
            Assert.Equal(PlatformCatalog.SampleApps.Count, apps.Count);
            Assert.All(apps, a => Assert.False(a.Enabled));
        }
    }

    [Fact]
    public void Admin_password_is_stored_hashed()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var repo = new AdminRepository(db);
            Assert.False(repo.Exists());
            repo.Create("phuhuynh", PasswordHasher.Hash("BiMat2024", 10_000));
            var admin = repo.Get()!;
            Assert.Equal("phuhuynh", admin.Username);
            Assert.DoesNotContain("BiMat2024", admin.PasswordHash);
            Assert.True(PasswordHasher.Verify("BiMat2024", admin.PasswordHash));
            SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(dir.Path, "test.db*"))
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.Latin1);
                Assert.DoesNotContain("BiMat2024", reader.ReadToEnd());
            }
        }
    }

    [Fact]
    public void Block_configuration_roundtrip()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var repo = new BlockRepository(db);
            repo.Save(new Dictionary<string, bool> { ["facebook"] = true, ["tiktok"] = false }, ["shopee.vn", "lazada.vn"]);
            repo.Save(new Dictionary<string, bool> { ["facebook"] = true, ["tiktok"] = true }, ["lazada.vn"]);
            Assert.Equal(["facebook", "tiktok"], repo.GetEnabledPlatformKeys().OrderBy(x => x));
            Assert.Equal(["lazada.vn"], repo.GetCustomDomains());
            repo.Save(new Dictionary<string, bool>(), []);
            Assert.Empty(repo.GetCustomDomains());
        }
    }

    [Fact]
    public void App_rules_crud_and_duplicate_protection()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var repo = new AppRuleRepository(db);
            Assert.True(repo.Add("Game thử", "CmcTestGame.exe", @"C:\Games\CmcTestGame.exe", true));
            Assert.False(repo.Add("Game 2", "cmctestgame.EXE", null, true));
            var rule = repo.GetAll().Single(r => r.ProcessName == "CmcTestGame.exe");
            Assert.Contains("CmcTestGame.exe", repo.GetEnabledProcessNames());
            repo.SetEnabled(rule.Id, false);
            Assert.DoesNotContain("CmcTestGame.exe", repo.GetEnabledProcessNames());
            repo.Delete(rule.Id);
            Assert.DoesNotContain(repo.GetAll(), r => r.Id == rule.Id);
        }
    }

    [Fact]
    public void Logs_filter_search_export_and_clear()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var logs = new LogRepository(db);
            logs.Info(LogCategory.Website, "Đã chặn facebook.com");
            logs.Warn(LogCategory.Admin, "Đăng nhập thất bại 100%_x");
            logs.Error(LogCategory.Service, "Lỗi dịch vụ");

            Assert.Equal(3, logs.Query(new LogQuery()).Count);
            Assert.Single(logs.Query(new LogQuery { Category = LogCategory.Admin }));
            Assert.Single(logs.Query(new LogQuery { Search = "100%_" }));
            Assert.Empty(logs.Query(new LogQuery { Search = "%" + "không có" }));
            Assert.Equal(3, logs.Query(new LogQuery { FromLocal = DateTime.Today, ToLocal = DateTime.Today }).Count);
            Assert.Empty(logs.Query(new LogQuery { ToLocal = DateTime.Today.AddDays(-2) }));

            var csv = Path.Combine(dir.Path, "log.csv");
            LogRepository.ExportCsv(logs.Query(new LogQuery()), csv);
            var text = File.ReadAllText(csv, Encoding.UTF8);
            Assert.Contains("Đã chặn facebook.com", text);

            logs.Clear();
            Assert.Equal(0, logs.Count());
        }
    }

    [Fact]
    public void System_change_store_keeps_first_original_only()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var store = new SystemChangeRepository(db);
            store.SaveOriginalIfAbsent("HKLM\\X\\Y", true, "3");
            store.SaveOriginalIfAbsent("HKLM\\X\\Y", true, "0");
            Assert.True(store.TryGetOriginal("HKLM\\X\\Y", out var existed, out var value));
            Assert.True(existed);
            Assert.Equal("3", value);
            store.Remove("HKLM\\X\\Y");
            Assert.False(store.TryGetOriginal("HKLM\\X\\Y", out _, out _));
        }
    }

    [Fact]
    public void Coordinator_applies_pauses_and_verifies_against_test_hosts()
    {
        var (dir, db) = Create();
        using (dir)
        {
            var hosts = Path.Combine(dir.Path, "hosts");
            File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
            var provider = new HostsBlockingProvider(hosts, Path.Combine(dir.Path, "bk"), flushDns: false);
            var c = new ProtectionCoordinator(db, provider, appLockerEnabled: false);

            c.Blocks.Save(new Dictionary<string, bool> { ["tiktok"] = true }, ["shopee.vn"]);
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Contains("tiktok.com", provider.GetAppliedDomains());
            Assert.Contains("m.shopee.vn", provider.GetAppliedDomains());
            Assert.Null(c.VerifyAndRepairWeb());

            // Mô phỏng người dùng xóa khối chặn: dịch vụ phải tự khôi phục.
            File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
            var repaired = c.VerifyAndRepairWeb();
            Assert.NotNull(repaired);
            Assert.True(repaired!.Success);
            Assert.Contains("tiktok.com", provider.GetAppliedDomains());

            c.Settings.SetBool(SettingKeys.ProtectionPaused, true);
            Assert.True(c.ApplyWebBlocking().Success);
            Assert.Empty(provider.GetAppliedDomains());
            Assert.Equal("127.0.0.1 localhost\r\n", File.ReadAllText(hosts));
            Assert.NotEmpty(c.Logs.Query(new LogQuery { Category = LogCategory.Service }));
        }
    }
}
