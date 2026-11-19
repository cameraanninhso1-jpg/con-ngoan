using System.Diagnostics;
using System.Text.Json;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaoMinhComputer.Service.Workers;

/// <summary>
/// Cập nhật phần mềm do dịch vụ (SYSTEM) thực hiện:
/// - Kiểm tra version.json mỗi ngày một lần (nếu phụ huynh không tắt) và khi phụ huynh bấm "Kiểm tra cập nhật".
/// - Chỉ tin manifest có chữ ký ECDSA hợp lệ của Cao Minh, chỉ nhận bản MỚI HƠN bản đang chạy.
/// - Cài khi phụ huynh bấm: tải bộ cài vào thư mục chỉ SYSTEM/Administrators ghi được, kiểm tra SHA-256, cài im lặng,
///   sau đó luôn khởi động lại dịch vụ (kể cả khi cài lỗi) và ghi nhật ký kết quả ở lần khởi động kế tiếp.
/// </summary>
public sealed class UpdateWorker(ILogger<UpdateWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        UpdateRunner runner;
        try
        {
            var db = new AppDatabase();
            db.Migrate();
            runner = UpdateRunner.Init(new ProtectionCoordinator(db));
            runner.ReportPendingResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateWorker không khởi tạo được.");
            return;
        }

        try
        {
            await Task.Delay(FirstCheckDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (runner.AutoCheckDue(CheckEvery)) await runner.CheckAsync(manual: false, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Kiểm tra cập nhật lỗi.");
                }
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class UpdateRunner
{
    private static UpdateRunner? _instance;
    private readonly ProtectionCoordinator _c;
    private int _busy;

    private UpdateRunner(ProtectionCoordinator c) => _c = c;

    public static UpdateRunner? Instance => _instance;

    public static UpdateRunner Init(ProtectionCoordinator c) => _instance ??= new UpdateRunner(c);

    /// <summary>Phiên bản đầy đủ (4 thành phần) của dịch vụ đang chạy.</summary>
    public static Version Current => UpdateVerifier.Normalize(typeof(UpdateRunner).Assembly.GetName().Version ?? new Version(1, 0));

    public sealed record Pending(string Version, string From, DateTime StartedUtc);

    public bool AutoCheckDue(TimeSpan every)
    {
        if (_c.Settings.Get(SettingKeys.UpdateAutoCheck) == "0") return false;
        var last = _c.Settings.Get(SettingKeys.UpdateLastCheck);
        return last is null || !DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ||
               DateTime.UtcNow - t >= every;
    }

    public async Task<UpdateFeed.CheckResult> CheckAsync(bool manual, CancellationToken ct = default)
    {
        var r = await UpdateFeed.CheckAsync(Current, ct).ConfigureAwait(false);
        _c.Settings.Set(SettingKeys.UpdateLastCheck, DateTime.UtcNow.ToString("O"));
        if (r.Ok && r.IsNewer)
        {
            var previous = _c.Settings.Get(SettingKeys.UpdateAvailable);
            var json = JsonSerializer.Serialize(r.Manifest);
            _c.Settings.Set(SettingKeys.UpdateAvailable, json);
            if (previous is null || !previous.Contains($"\"{r.Manifest!.Version}\""))
                _c.Logs.Info(LogCategory.Service, $"Có phiên bản mới {r.Manifest!.Version} (đã xác minh chữ ký Cao Minh). Vào Giới thiệu › Cập nhật để cài.");
            _c.Settings.Set(SettingKeys.UpdateStatus, r.Message);
        }
        else if (r.Ok)
        {
            _c.Settings.Delete(SettingKeys.UpdateAvailable);
            _c.Settings.Set(SettingKeys.UpdateStatus, r.Message);
        }
        else
        {
            _c.Settings.Set(SettingKeys.UpdateStatus, r.Message);
            // Chữ ký sai là dấu hiệu bị giả mạo → luôn ghi nhật ký; lỗi mạng chỉ ghi khi phụ huynh tự bấm kiểm tra.
            if (r.Message.Contains("Chữ ký")) _c.Logs.Error(LogCategory.Service, r.Message);
            else if (manual) _c.Logs.Warn(LogCategory.Service, r.Message);
        }
        return r;
    }

    /// <summary>Bắt đầu cài bản mới ở nền. Trả về thông báo cho giao diện.</summary>
    public string StartInstall(string requestedBy)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return "Đang cập nhật, vui lòng chờ...";
        _ = Task.Run(async () =>
        {
            try { await InstallAsync(requestedBy).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Fail("Cập nhật lỗi: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
        return "Đã bắt đầu cập nhật.";
    }

    private void Status(string text) => _c.Settings.Set(SettingKeys.UpdateStatus, text);

    private void Fail(string message)
    {
        Status(message);
        _c.Logs.Error(LogCategory.Service, message);
    }

    private async Task InstallAsync(string requestedBy)
    {
        Status("Đang kiểm tra phiên bản mới...");
        var check = await CheckAsync(manual: true).ConfigureAwait(false);
        if (!check.Ok) { Status(check.Message); return; }
        if (!check.IsNewer || check.Manifest is not { } m) { Status(check.Message); return; }

        Status($"Đang tải bộ cài phiên bản {m.Version}...");
        var (path, error) = await UpdateFeed.DownloadAsync(m).ConfigureAwait(false);
        if (path is null) { Fail(error ?? "Không tải được bộ cài."); return; }

        var from = UpdateVerifier.FormatVersion(Current);
        _c.Settings.Set(SettingKeys.UpdatePending, JsonSerializer.Serialize(new Pending(m.Version, from, DateTime.UtcNow)));
        _c.Logs.Info(LogCategory.Service,
            $"{requestedBy} cài bản cập nhật {from} → {m.Version} (chữ ký và SHA-256 hợp lệ). Dịch vụ sẽ khởi động lại trong ít phút.");
        Status($"Đang cài phiên bản {m.Version} – dịch vụ sẽ khởi động lại, mất khoảng 1–2 phút...");

        // Bộ cài sẽ dừng dịch vụ này; cmd chạy tiếp và luôn khởi động lại dịch vụ sau khi cài (kể cả khi cài lỗi).
        var log = Path.Combine(UpdateFeed.UpdatesDir, "setup.log");
        var sys = Environment.SystemDirectory;
        var command = $"/d /s /c \"\"{path}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"{log}\" " +
                      $"& \"{Path.Combine(sys, "sc.exe")}\" start {AppPaths.ServiceName}\"";
        Process.Start(new ProcessStartInfo(Path.Combine(sys, "cmd.exe"), command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdateFeed.UpdatesDir
        });
    }

    /// <summary>Gọi khi dịch vụ khởi động: ghi kết quả của lần cập nhật trước (nếu có).</summary>
    public void ReportPendingResult()
    {
        var json = _c.Settings.Get(SettingKeys.UpdatePending);
        if (json is null) return;
        _c.Settings.Delete(SettingKeys.UpdatePending);
        Pending? p;
        try { p = JsonSerializer.Deserialize<Pending>(json); }
        catch (JsonException) { return; }
        if (p is null || !Version.TryParse(p.Version, out var target)) return;

        var now = UpdateVerifier.FormatVersion(Current);
        if (Current >= UpdateVerifier.Normalize(target))
        {
            _c.Logs.Info(LogCategory.Service, $"Đã cập nhật thành công lên phiên bản {now} (từ {p.From}).");
            _c.Settings.Delete(SettingKeys.UpdateAvailable);
            Status($"Đã cập nhật lên phiên bản {now}.");
        }
        else
        {
            Fail($"Cập nhật lên {p.Version} KHÔNG thành công – vẫn dùng phiên bản {now}. Chi tiết: {Path.Combine(UpdateFeed.UpdatesDir, "setup.log")}.");
        }
        try
        {
            foreach (var f in Directory.EnumerateFiles(UpdateFeed.UpdatesDir, "*.exe")) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
