using System.Diagnostics;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaoMinhComputer.Service.Workers;

/// <summary>
/// Dịch vụ nền nhẹ:
/// - Theo dõi file HOSTS theo sự kiện (FileSystemWatcher) và tự khôi phục khối chặn nếu bị sửa/xóa.
/// - Kiểm tra lại định kỳ 10 phút (phòng khi bỏ lỡ sự kiện) và ghi heartbeat 5 phút/lần.
/// - Nếu phụ huynh bật "tự đóng ứng dụng bị chặn": 20 giây/lần đóng tiến trình bị chặn thuộc tài khoản của con.
/// Không polling dày; khi nhàn rỗi CPU ~0%.
/// </summary>
public sealed class ProtectionWorker(ILogger<ProtectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan VerifyInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KillLogThrottle = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _hostsChanged = new(0);
    private int _sessionChanged;
    private readonly Dictionary<string, DateTime> _lastKillLog = new(StringComparer.OrdinalIgnoreCase);
    private ProtectionCoordinator? _c;
    private FileSystemWatcher? _watcher;
    private RealtimeProcessWatcher? _processWatcher;
    private DateTime _lastVerify = DateTime.MinValue;
    private DateTime _lastHeartbeat = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            var db = new AppDatabase();
            db.Migrate();
            _c = new ProtectionCoordinator(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không khởi tạo được database của Con Ngoan.");
            throw;
        }

        _c.Logs.Info(LogCategory.Service, "Dịch vụ bảo vệ khởi động.");
        StartWatcher();
        StartProcessWatcher();
        // Đổi người dùng → áp dụng lại chặn website ngay (không chờ gom sự kiện) để con mở trình duyệt là đã bị chặn.
        ServiceSignals.SessionChanged += () =>
        {
            Interlocked.Exchange(ref _sessionChanged, 1);
            _hostsChanged.Release();
            // Kiểm tra lại sau 5 giây: lúc nhận sự kiện, trạng thái phiên có thể chưa chuyển sang "đang hoạt động".
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _sessionChanged, 1);
                _hostsChanged.Release();
            }, TaskScheduler.Default);
        };
        SafeRun(Verify);
        SafeRun(Heartbeat);

        while (!stoppingToken.IsCancellationRequested)
        {
            var enforceApps = ShouldEnforceApps();
            var wait = enforceApps ? ProcessInterval : VerifyInterval;
            // Đang "mượn máy": thức dậy đúng lúc hết hạn để bật lại bảo vệ (không phụ thuộc Telegram).
            if (_c.PausedUntilUtc is { } until)
            {
                var left = until - DateTime.UtcNow + TimeSpan.FromSeconds(1);
                if (left < wait) wait = left < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left;
            }
            bool signaled;
            try
            {
                signaled = await _hostsChanged.WaitAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (signaled)
            {
                // Gom các sự kiện liên tiếp của file HOSTS (một lần ghi file sinh nhiều sự kiện); sự kiện phiên thì xử lý ngay.
                if (Interlocked.Exchange(ref _sessionChanged, 0) == 0)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
                while (_hostsChanged.CurrentCount > 0) _hostsChanged.Wait(0);
                SafeRun(Verify);
            }
            else if (DateTime.UtcNow - _lastVerify >= VerifyInterval)
            {
                SafeRun(Verify);
            }

            SafeRun(() =>
            {
                if (_c.ResumeIfPauseExpired())
                    ServiceSignals.RaiseParentNotice("⏰ Hết thời gian mượn máy – Con Ngoan đã tự bật lại bảo vệ cho con.");
            });
            if (enforceApps) SafeRun(EnforceApps);
            SafeRun(SuperviseAgent);
            if (DateTime.UtcNow - _lastHeartbeat >= HeartbeatInterval) SafeRun(Heartbeat);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _processWatcher?.Dispose();
        try
        {
            _c?.Logs.Info(LogCategory.Service, "Dịch vụ bảo vệ dừng.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không ghi được nhật ký khi dừng dịch vụ.");
        }
        await base.StopAsync(cancellationToken);
    }

    private void StartProcessWatcher()
    {
        try
        {
            _processWatcher = new RealtimeProcessWatcher(
                getBlockedNames: () =>
                {
                    if (_c is null || _c.IsPaused || _c.IsInBreakNow() ||
                        !_c.Settings.GetBool(SettingKeys.RealtimeProcessBlocking, fallback: true)) return new HashSet<string>();
                    return _c.Apps.GetEnabledProcessNames()
                        .Where(n => !ProtectedProcesses.IsProtected(n))
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
                },
                getChildSids: () =>
                {
                    if (_c is null || _c.IsPaused) return new HashSet<string>();
                    return _c.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
                },
                onProcessKilled: (name, _) =>
                {
                    // Giới hạn ghi nhật ký: con mở lại liên tục không làm đầy nhật ký.
                    lock (_lastKillLog)
                    {
                        if (_lastKillLog.TryGetValue(name, out var last) && DateTime.UtcNow - last < KillLogThrottle) return;
                        _lastKillLog[name] = DateTime.UtcNow;
                    }
                    _c?.Logs.Info(LogCategory.Application, $"Đã đóng {name} (tài khoản của con) ngay khi vừa mở vì ứng dụng đang bị chặn.");
                },
                logError: msg => logger.LogWarning("{Message}", msg)
            );
            _processWatcher.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể khởi động giám sát tiến trình thời gian thực.");
        }
    }

    private void StartWatcher()
    {
        var hosts = HostsBlockingProvider.DefaultHostsPath;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(hosts)!, Path.GetFileName(hosts))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false
            };
            FileSystemEventHandler onChange = (_, _) => _hostsChanged.Release();
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Renamed += (_, _) => _hostsChanged.Release();
            _watcher.Error += (_, _) => _hostsChanged.Release();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không theo dõi được file HOSTS; chuyển sang kiểm tra định kỳ.");
            _c?.Logs.Warn(LogCategory.Service, "Không theo dõi được file HOSTS theo sự kiện, chỉ kiểm tra định kỳ: " + ex.Message);
        }
    }

    private DateTime _lastAppLockerCheck = DateTime.MinValue;

    private void Verify()
    {
        _lastVerify = DateTime.UtcNow;
        _c!.VerifyAndRepairWeb();
        if (_c.ApplyForNewProfiles()) _lastAppLockerCheck = DateTime.UtcNow;
        if (DateTime.UtcNow - _lastAppLockerCheck >= VerifyInterval) VerifyAppLocker();
    }

    /// <summary>Đảm bảo dịch vụ Application Identity chạy và quy tắc AppLocker của ta còn nguyên (khi đang cần).</summary>
    private void VerifyAppLocker()
    {
        _lastAppLockerCheck = DateTime.UtcNow;
        var c = _c!;
        if (!c.AppLockerAvailable || c.IsPaused || c.Settings.GetChildSids().Count == 0) return;
        if (!c.StrictMode && c.Apps.GetEnabledProcessNames().Count == 0) return;

        if (!AppLockerManager.IsAppIdServiceRunning())
        {
            var started = AppLockerManager.EnsureAppIdService();
            c.Logs.Add(started ? CaoMinhComputer.Core.Models.LogLevel.Warning : CaoMinhComputer.Core.Models.LogLevel.Error, LogCategory.Service, started
                ? "Dịch vụ Application Identity (AppLocker) bị dừng – đã khởi động lại."
                : "Không khởi động được dịch vụ Application Identity – AppLocker chưa có hiệu lực.");
        }

        var xml = AppLockerManager.GetMdmPolicy(out var error);
        if (error is not null)
        {
            c.Logs.Error(LogCategory.Service, "Không đọc được chính sách AppLocker: " + error);
            return;
        }
        if (!AppLockerManager.HasOurRules(xml))
        {
            var result = c.ApplyAppLocker();
            c.Logs.Add(result.Success ? CaoMinhComputer.Core.Models.LogLevel.Warning : CaoMinhComputer.Core.Models.LogLevel.Error, LogCategory.Service,
                "Quy tắc AppLocker của Con Ngoan bị xóa ngoài ý muốn – " + (result.Success ? "đã khôi phục." : "không khôi phục được: " + result.Message));
        }
    }

    private void Heartbeat()
    {
        _lastHeartbeat = DateTime.UtcNow;
        _c!.Settings.SetUtc(SettingKeys.ServiceHeartbeat, DateTime.UtcNow);
    }

    private void SuperviseAgent()
    {
        if (_c is null || _c.IsPaused) return;
        var supervise = _c.Settings.GetBool(SettingKeys.SecuritySuperviseAgent, fallback: true);
        if (!supervise) return;

        var children = _c.Settings.GetChildSids();
        if (children.Count == 0) return;

        var agentPath = Path.Combine(AppContext.BaseDirectory, AgentProtocol.AgentExeName);
        SessionProcessLauncher.SuperviseAgentInChildSessions(children, agentPath);
    }

    private bool ShouldEnforceApps()
    {
        try
        {
            return _c is not null && !_c.IsPaused && _c.Settings.GetBool(SettingKeys.AppAutoClose) &&
                   _c.Settings.GetChildSids().Count > 0 && !_c.IsInBreakNow();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Đóng tiến trình bị chặn – chỉ khi tiến trình thuộc tài khoản của con, không bao giờ đụng tới tiến trình hệ thống.</summary>
    private void EnforceApps()
    {
        var names = _c!.Apps.GetEnabledProcessNames()
            .Where(n => !ProtectedProcesses.IsProtected(n))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return;
        var children = _c.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!names.Contains(process.ProcessName)) continue;
                var owner = ProcessOwner.GetOwnerSid(process.Id);
                if (owner is null || !children.Contains(owner)) continue;
                try
                {
                    process.Kill();
                    var key = process.ProcessName;
                    bool log;
                    lock (_lastKillLog)
                    {
                        log = !_lastKillLog.TryGetValue(key, out var last) || DateTime.UtcNow - last > KillLogThrottle;
                        if (log) _lastKillLog[key] = DateTime.UtcNow;
                    }
                    if (log) _c.Logs.Info(LogCategory.Application, $"Đã đóng {key}.exe (tài khoản của con) vì ứng dụng đang bị chặn.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Tiến trình đã thoát hoặc được bảo vệ – bỏ qua.
                }
            }
        }
    }

    private void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi trong dịch vụ Con Ngoan.");
            try { _c?.Logs.Error(LogCategory.Service, "Lỗi dịch vụ: " + ex.Message); }
            catch (Exception) { /* database không khả dụng */ }
        }
    }
}
