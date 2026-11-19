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
/// Quản lý thời gian sử dụng:
/// - Chỉ tính thời gian khi phiên của con đang hoạt động và KHÔNG khóa màn hình (khác với "máy đang bật").
/// - Đếm bằng đồng hồ đơn điệu (Stopwatch), giới hạn mỗi nhịp, bỏ khoảng sleep/hibernate → đổi giờ hệ thống không ảnh hưởng.
/// - Lịch tính theo múi giờ phụ huynh cấu hình; lưu vào SQLite mỗi nhịp → khởi động lại không mất dữ liệu.
/// - Cảnh báo trước 5 phút và 1 phút; hết giờ: thông báo, chờ ân hạn rồi khóa (ngắt phiên) hoặc đăng xuất.
/// - Giờ giải lao: tự mở/chặn lại mạng xã hội, game; tùy chọn đóng trình duyệt khi hết giờ giải lao.
/// Nhịp 30 giây khi con đang dùng máy, 60 giây khi không; đánh thức ngay khi có sự kiện phiên/nguồn điện.
/// </summary>
public sealed class TimeWorker(ILogger<TimeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(60);
    private const int MaxCountedSecondsPerTick = 75;
    private const int WarnFiveMinutes = 330;
    private const int WarnOneMinute = 70;
    private const string Title = "Con Ngoan";

    private sealed class Track
    {
        public required string Sid;
        public required string UserName;
        public required int SessionId;
        public long DbId;
        public int Seconds;
        public long LastTimestamp;
        public long ResumeSeen;
        public required string Day;
        public bool Warned5;
        public bool Warned1;
        public DateTime? GraceDeadlineUtc;
    }

    private readonly Dictionary<int, Track> _tracks = [];
    private readonly Dictionary<string, DateTime> _lastEnforcedUtc = new(StringComparer.OrdinalIgnoreCase);
    private ProtectionCoordinator? _c;
    private DateTime _lastUtc;
    private long _lastTimestamp;
    private long _lastResumeCount;
    private bool _breakWarned5;
    private bool _breakWarned1;
    private string _lastPruneDay = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            AppPaths.EnsureDataDirectory();
            var db = new AppDatabase();
            db.Migrate();
            _c = new ProtectionCoordinator(db);
            var closed = _c.Usage.CloseDanglingSessions();
            if (closed > 0) _c.Logs.Warn(LogCategory.Time, $"Đóng {closed} phiên sử dụng bị gián đoạn (máy tắt đột ngột hoặc dịch vụ dừng).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TimeWorker không khởi tạo được.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = IdleInterval;
            try
            {
                interval = Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi xử lý thời gian sử dụng.");
                try { _c.Logs.Error(LogCategory.Service, "Lỗi quản lý thời gian: " + ex.Message); }
                catch (Exception) { /* database không khả dụng */ }
            }

            try
            {
                await ServiceSignals.TimeWake.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var track in _tracks.Values.ToList()) EndTrack(track, DateTime.UtcNow, "dịch vụ dừng");
    }

    private TimeSpan Tick()
    {
        var c = _c!;
        var utc = DateTime.UtcNow;
        var now = Stopwatch.GetTimestamp();
        DetectClockChange(utc, now);

        var config = c.Settings.GetTimeConfig();
        var local = TimePolicyEngine.ToScheduleTime(config, utc);
        var day = TimePolicyEngine.DayKey(local);
        if (day != _lastPruneDay)
        {
            _lastPruneDay = day;
            c.Usage.Prune();
        }

        var children = c.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sessions = WindowsSessions.Enumerate();
        var activeChild = sessions
            .Where(s => s.Sid is not null && children.Contains(s.Sid) && s.IsActive && !s.IsLocked)
            .ToList();

        // Kết thúc theo dõi các phiên đã khóa/ngắt/đăng xuất.
        foreach (var track in _tracks.Values.ToList())
        {
            if (!activeChild.Any(s => s.SessionId == track.SessionId && s.Sid == track.Sid))
                EndTrack(track, utc, "khóa màn hình, ngắt hoặc đăng xuất");
        }

        // Cộng thời gian sử dụng (mỗi tài khoản chỉ cộng một lần mỗi nhịp).
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in activeChild)
        {
            if (!_tracks.TryGetValue(s.SessionId, out var track))
            {
                StartTrack(s, utc, now, day);
                continue;
            }

            var delta = (int)Math.Round(Stopwatch.GetElapsedTime(track.LastTimestamp, now).TotalSeconds);
            track.LastTimestamp = now;
            if (track.ResumeSeen != ServiceSignals.ResumeCount)
            {
                track.ResumeSeen = ServiceSignals.ResumeCount;
                delta = 0; // không tính thời gian máy ngủ
            }
            delta = Math.Clamp(delta, 0, MaxCountedSecondsPerTick);

            // Không tính lúc con để máy mở mà không thao tác (Agent báo; video đang phát vẫn tính là đang dùng).
            if (delta > 0 && c.IdleExclusionEnabled && Ipc.AgentRegistry.IsIdle(track.SessionId, c.IdleThresholdSeconds))
                delta = 0;

            if (track.Day != day)
            {
                track.Day = day;
                track.Warned5 = track.Warned1 = false;
                track.GraceDeadlineUtc = null;
            }

            if (delta > 0 && counted.Add(track.Sid)) c.Usage.AddUsed(track.Sid, day, delta);
            track.Seconds += delta;
            c.Usage.UpdateSession(track.DbId, track.Seconds, null);
        }

        var next = activeChild.Count > 0 ? ActiveInterval : IdleInterval;
        foreach (var s in activeChild)
        {
            var due = Enforce(_tracks[s.SessionId], config, utc);
            if (due < next) next = due;
        }

        HandleBreak(config, local, sessions, children);
        return next < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : next;
    }

    private void StartTrack(UserSession s, DateTime utc, long timestamp, string day)
    {
        var track = new Track
        {
            Sid = s.Sid!,
            UserName = s.UserName,
            SessionId = s.SessionId,
            LastTimestamp = timestamp,
            ResumeSeen = ServiceSignals.ResumeCount,
            Day = day
        };
        track.DbId = _c!.Usage.StartSession(track.Sid, utc);
        _tracks[s.SessionId] = track;
        _c.Logs.Info(LogCategory.Time, $"Bắt đầu phiên sử dụng của {s.UserName} (phiên {s.SessionId}).");
    }

    private void EndTrack(Track track, DateTime utc, string reason)
    {
        _tracks.Remove(track.SessionId);
        try
        {
            _c!.Usage.UpdateSession(track.DbId, track.Seconds, utc);
            _c.Logs.Info(LogCategory.Time,
                $"Kết thúc phiên sử dụng của {track.UserName}: {TimePolicyEngine.FormatDuration(track.Seconds)} ({reason}).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không ghi được kết thúc phiên.");
        }
    }

    /// <summary>Cảnh báo/khóa theo tình trạng thời gian. Trả về thời điểm cần kiểm tra lại sớm nhất.</summary>
    private TimeSpan Enforce(Track track, TimeConfig config, DateTime utc)
    {
        var c = _c!;
        var status = c.GetTimeStatus(track.Sid, utc);
        if (status.Allowed)
        {
            track.GraceDeadlineUtc = null;
            if (status.RemainingSeconds is not { } remaining) return ActiveInterval;
            if (remaining > WarnFiveMinutes)
            {
                track.Warned5 = track.Warned1 = false;
            }
            else if (remaining <= WarnOneMinute && !track.Warned1)
            {
                track.Warned1 = track.Warned5 = true;
                Notify(track, $"Chỉ còn khoảng 1 phút sử dụng máy. Hãy lưu bài ngay!\n{status.Description}");
            }
            else if (!track.Warned5)
            {
                track.Warned5 = true;
                Notify(track, $"Còn khoảng {TimePolicyEngine.FormatDuration(remaining)} sử dụng máy. Hãy chuẩn bị lưu bài.\n{status.Description}");
            }
            // Nhịp kế tiếp ngay khi hết giờ để thông báo đúng lúc.
            return TimeSpan.FromSeconds(Math.Clamp(remaining + 1, 5, (int)ActiveInterval.TotalSeconds));
        }

        var verb = config.Action == LimitAction.Logoff ? "đăng xuất" : "khóa";
        if (track.GraceDeadlineUtc is null)
        {
            var recentlyEnforced = _lastEnforcedUtc.TryGetValue(track.Sid, out var last) && utc - last < TimeSpan.FromMinutes(10);
            var grace = recentlyEnforced
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromMinutes(Math.Max(0.5, config.GraceMinutes));
            track.GraceDeadlineUtc = utc + grace;
            Notify(track, $"{status.Description}\n\nMáy sẽ {verb} sau {TimePolicyEngine.FormatDuration((int)grace.TotalSeconds)}. Hãy lưu bài ngay.");
            c.Logs.Info(LogCategory.Time, $"{track.UserName}: {status.Description} Sẽ {verb} sau {TimePolicyEngine.FormatDuration((int)grace.TotalSeconds)}.");
            return grace;
        }

        if (utc < track.GraceDeadlineUtc) return track.GraceDeadlineUtc.Value - utc;

        var ok = config.Action == LimitAction.Logoff
            ? WindowsSessions.Logoff(track.SessionId)
            : WindowsSessions.Disconnect(track.SessionId);
        _lastEnforcedUtc[track.Sid] = utc;
        track.GraceDeadlineUtc = utc + TimeSpan.FromSeconds(30);
        if (ok)
            c.Logs.Info(LogCategory.Time, $"Đã {verb} phiên của {track.UserName}: {status.Description}");
        else
            c.Logs.Error(LogCategory.Time, $"Không {verb} được phiên của {track.UserName} (mã lỗi {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        ServiceSignals.Wake();
        return ActiveInterval;
    }

    private void Notify(Track track, string message)
    {
        if (!WindowsSessions.SendMessage(track.SessionId, Title, message, timeoutSeconds: 90))
            logger.LogWarning("Không gửi được thông báo tới phiên {Session}.", track.SessionId);
    }

    private void HandleBreak(TimeConfig config, DateTime local, IReadOnlyList<UserSession> sessions, HashSet<string> children)
    {
        var c = _c!;
        var inBreak = TimePolicyEngine.IsInBreak(config, local, out var secondsLeft);
        var applied = c.Settings.GetBool(SettingKeys.BreakApplied);
        var childSessions = sessions.Where(s => s.Sid is not null && children.Contains(s.Sid)).ToList();

        if (inBreak != applied)
        {
            c.Settings.SetBool(SettingKeys.BreakApplied, inBreak);
            var web = c.ApplyWebBlocking();
            var app = c.ApplyAppBlocking();
            c.Logs.Info(LogCategory.Time, inBreak
                ? $"Bắt đầu giờ giải lao: tạm mở mạng xã hội, website và game. {web.Message}"
                : $"Hết giờ giải lao: chặn lại theo cài đặt. {web.Message} {app.Message}");
            _breakWarned5 = _breakWarned1 = false;

            if (!inBreak && config.CloseBrowsersAfterBreak && childSessions.Count > 0)
            {
                var closed = WindowsSessions.CloseBrowsers(childSessions.Select(s => s.SessionId).ToHashSet());
                if (closed > 0) c.Logs.Info(LogCategory.Time, $"Đã đóng {closed} tiến trình trình duyệt của con khi hết giờ giải lao.");
            }
        }

        if (!inBreak || !config.CloseBrowsersAfterBreak) return;
        var message = secondsLeft <= WarnOneMinute && !_breakWarned1
            ? "Còn khoảng 1 phút là hết giờ giải lao. Trình duyệt sẽ được đóng – hãy lưu bài!"
            : secondsLeft <= WarnFiveMinutes && !_breakWarned5
                ? "Còn khoảng 5 phút là hết giờ giải lao. Mạng xã hội/game sẽ bị chặn lại và trình duyệt sẽ được đóng."
                : null;
        if (message is null) return;
        if (secondsLeft <= WarnOneMinute) _breakWarned1 = true;
        _breakWarned5 = true;
        foreach (var s in childSessions.Where(s => s.IsActive && !s.IsLocked))
            WindowsSessions.SendMessage(s.SessionId, Title, message, 60);
    }

    private void DetectClockChange(DateTime utc, long timestamp)
    {
        if (_lastTimestamp != 0 && _lastResumeCount == ServiceSignals.ResumeCount)
        {
            var monotonic = Stopwatch.GetElapsedTime(_lastTimestamp, timestamp);
            var wall = utc - _lastUtc;
            var drift = (wall - monotonic).Duration();
            if (drift > TimeSpan.FromMinutes(2))
                _c!.Logs.Warn(LogCategory.Time,
                    $"Phát hiện đồng hồ hệ thống bị thay đổi (lệch {Math.Round(drift.TotalMinutes)} phút). Thời gian sử dụng vẫn được đếm theo đồng hồ nội bộ.");
        }
        _lastUtc = utc;
        _lastTimestamp = timestamp;
        _lastResumeCount = ServiceSignals.ResumeCount;
    }
}
