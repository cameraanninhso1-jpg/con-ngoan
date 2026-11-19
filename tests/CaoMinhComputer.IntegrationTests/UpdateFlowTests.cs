using System.Diagnostics;
using System.ServiceProcess;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Win32;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>Chỉ chạy trong Sandbox cập nhật (scripts\E2E-Update.wsb) vì test này nâng cấp thật phiên bản đã cài.</summary>
public sealed class UpdateSandboxFactAttribute : FactAttribute
{
    public UpdateSandboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CMC_E2E_UPDATE") != "1")
            Skip = "Chỉ chạy trong Windows Sandbox cập nhật (CMC_E2E_UPDATE=1).";
    }
}

/// <summary>
/// Test đầu-cuối cập nhật phần mềm: dùng bộ cài thử nghiệm 1.4.0.99 được ký bằng khóa thật của Cao Minh (scripts\prepare-update-e2e.ps1).
/// 1) manifest bị sửa → từ chối; 2) bộ cài sai SHA-256 → không cài, dịch vụ vẫn chạy bản cũ; 3) bản hợp lệ → dịch vụ tự cài và khởi động lại.
/// </summary>
[Collection("E2E")]
public class UpdateFlowTests
{
    private const string Feed = @"C:\cmc\artifacts\e2e-update";
    private const string TargetVersion = "1.4.0.99";

    private static ProtectionCoordinator Real()
    {
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db) { ServiceExePath = SandboxFactAttribute.InstalledService };
    }

    private static void SetFeed(string? dir)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UpdateFeed.RegistryKey);
        if (dir is null) key.DeleteValue(UpdateFeed.RegistryValue, false);
        else key.SetValue(UpdateFeed.RegistryValue, new Uri(Path.Combine(dir, "version.json")).AbsoluteUri);
    }

    private static string InstalledVersion() => FileVersionInfo.GetVersionInfo(SandboxFactAttribute.InstalledService).FileVersion ?? "";

    private static bool Eventually(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (condition()) return true; } catch (Exception) { }
            Thread.Sleep(2000);
        }
        return condition();
    }

    [UpdateSandboxFact]
    public async Task Service_rejects_tampered_updates_and_installs_signed_update()
    {
        var c = Real();
        var before = InstalledVersion();
        Assert.StartsWith("1.4.0", before);
        Assert.NotEqual(TargetVersion, before);
        try
        {
            // 1) version.json bị sửa sau khi ký → chữ ký sai → không báo có bản mới, ghi nhật ký lỗi.
            var tampered = Path.Combine(Path.GetTempPath(), "cmc-feed-tampered");
            Directory.CreateDirectory(tampered);
            File.Copy(Path.Combine(Feed, "feed", "version.json.sig"), Path.Combine(tampered, "version.json.sig"), true);
            File.WriteAllText(Path.Combine(tampered, "version.json"),
                File.ReadAllText(Path.Combine(Feed, "feed", "version.json")).Replace("1.4.0.99", "1.4.0.98"));
            SetFeed(tampered);
            var r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateCheck, SandboxFactAttribute.InstalledService, 60_000);
            Assert.NotNull(r);
            Assert.False(r!.Ok);
            Assert.Contains("Chữ ký", r.Message);
            Assert.Null(c.Settings.Get(SettingKeys.UpdateAvailable));

            // 2) Manifest ký hợp lệ nhưng bộ cài không khớp SHA-256 → không cài, dịch vụ vẫn chạy bản cũ.
            SetFeed(Path.Combine(Feed, "feed-badhash"));
            r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateCheck, SandboxFactAttribute.InstalledService, 60_000);
            Assert.True(r?.Ok, r?.Message);
            Assert.Contains(TargetVersion, c.Settings.Get(SettingKeys.UpdateAvailable));
            r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateInstall, SandboxFactAttribute.InstalledService, 10_000);
            Assert.True(r?.Ok, r?.Message);
            Assert.True(Eventually(() => c.Settings.Get(SettingKeys.UpdateStatus)?.Contains("SHA-256") == true, 120),
                "Không báo lỗi SHA-256: " + c.Settings.Get(SettingKeys.UpdateStatus));
            Assert.Null(c.Settings.Get(SettingKeys.UpdatePending));
            Assert.Equal(before, InstalledVersion());
            Assert.Empty(Directory.GetFiles(UpdateFeed.UpdatesDir, "*.exe"));

            // 3) Bản hợp lệ → dịch vụ tải, cài im lặng, khởi động lại và ghi nhật ký thành công.
            SetFeed(Path.Combine(Feed, "feed"));
            r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateCheck, SandboxFactAttribute.InstalledService, 60_000);
            Assert.True(r?.Ok, r?.Message);
            r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateInstall, SandboxFactAttribute.InstalledService, 10_000);
            Assert.True(r?.Ok, r?.Message);
            Assert.True(Eventually(() => InstalledVersion() == TargetVersion, 300), "Không cài được bản mới: " + InstalledVersion());
            Assert.True(Eventually(() =>
            {
                using var sc = new ServiceController(AppPaths.ServiceName);
                return sc.Status == ServiceControllerStatus.Running &&
                       c.Logs.Query(new LogQuery { Category = LogCategory.Service })
                           .Any(l => l.Message.Contains($"Đã cập nhật thành công lên phiên bản {TargetVersion}"));
            }, 180), "Dịch vụ không khởi động lại / không ghi nhật ký cập nhật thành công. Trạng thái: " + c.Settings.Get(SettingKeys.UpdateStatus));
            Assert.Null(c.Settings.Get(SettingKeys.UpdateAvailable));
            Assert.NotNull(await PipeProtocol.SendAsync(PipeProtocol.CmdPing, SandboxFactAttribute.InstalledService, 5000));

            // Đã ở bản mới nhất → kiểm tra lại không báo bản mới nữa.
            r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateCheck, SandboxFactAttribute.InstalledService, 60_000);
            Assert.True(r?.Ok, r?.Message);
            Assert.Contains("mới nhất", r!.Message);
        }
        finally
        {
            SetFeed(null);
        }
    }
}
