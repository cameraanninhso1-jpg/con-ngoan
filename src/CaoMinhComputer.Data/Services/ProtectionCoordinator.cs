using CaoMinhComputer.Core.Interfaces;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;

namespace CaoMinhComputer.Data.Services;

/// <summary>
/// Điều phối việc áp dụng/kiểm tra/khôi phục chính sách giữa database và hệ thống.
/// Được dùng chung bởi giao diện (khi phụ huynh bấm Lưu &amp; áp dụng) và dịch vụ nền (tự phục hồi).
/// </summary>
public sealed class ProtectionCoordinator
{
    public AppDatabase Database { get; }
    public SettingsRepository Settings { get; }
    public AdminRepository Admin { get; }
    public BlockRepository Blocks { get; }
    public AppRuleRepository Apps { get; }
    public LogRepository Logs { get; }
    public SystemChangeRepository Changes { get; }
    public UsageRepository Usage { get; }
    public ExtraRequestRepository Requests { get; }
    public IWebsiteBlockingProvider WebProvider { get; }
    public InstallPolicyService InstallPolicy { get; }
    public BrowserPolicyService BrowserPolicy { get; }

    private readonly bool? _appLockerEnabled;
    private WindowsEditionInfo? _edition;

    /// <param name="appLockerEnabled">null = tự nhận theo edition Windows; false = tắt (dùng trong kiểm thử).</param>
    public ProtectionCoordinator(AppDatabase db, IWebsiteBlockingProvider? webProvider = null, bool? appLockerEnabled = null)
    {
        _appLockerEnabled = appLockerEnabled;
        Database = db;
        Settings = new SettingsRepository(db);
        Admin = new AdminRepository(db);
        Blocks = new BlockRepository(db);
        Apps = new AppRuleRepository(db);
        Logs = new LogRepository(db);
        Changes = new SystemChangeRepository(db);
        Usage = new UsageRepository(db);
        Requests = new ExtraRequestRepository(db);
        WebProvider = webProvider ?? new HostsBlockingProvider();
        InstallPolicy = new InstallPolicyService(Changes);
        BrowserPolicy = new BrowserPolicyService(Changes);
    }

    public bool IsPaused
    {
        get
        {
            if (Settings.GetBool(SettingKeys.ProtectionPaused)) return true;
            return Settings.GetUtc(SettingKeys.ProtectionPausedUntilUtc) is { } until && DateTime.UtcNow < until;
        }
    }

    /// <summary>Thời điểm hết hạn "mượn máy" (null nếu không mượn).</summary>
    public DateTime? PausedUntilUtc => Settings.GetUtc(SettingKeys.ProtectionPausedUntilUtc);

    /// <summary>
    /// Hết thời gian mượn máy → bật lại web, ứng dụng, chính sách trình duyệt. Dịch vụ gọi định kỳ (không phụ thuộc Telegram).
    /// Trả về true nếu vừa bật lại.
    /// </summary>
    public bool ResumeIfPauseExpired()
    {
        if (PausedUntilUtc is not { } until || DateTime.UtcNow < until) return false;
        Settings.Delete(SettingKeys.ProtectionPausedUntilUtc);
        if (Settings.GetBool(SettingKeys.ProtectionPaused)) return false; // phụ huynh đã chủ động tắt hẳn
        Logs.Info(LogCategory.Admin, "Hết thời gian mượn máy – tự động bật lại bảo vệ (website, ứng dụng, trình duyệt).");
        ApplyWebBlocking();
        ApplyAppBlocking();
        return true;
    }

    /// <summary>Tạm mở bảo vệ trong một khoảng thời gian (ví dụ bố mẹ mượn máy), sau đó tự động bật lại.</summary>
    public IReadOnlyList<ApplyResult> PauseProtectionFor(TimeSpan duration, string reason = "Phụ huynh mượn máy")
    {
        var minutes = Math.Clamp(duration.TotalMinutes, 5, 240);
        var until = DateTime.UtcNow.AddMinutes(minutes);
        Settings.SetBool(SettingKeys.ProtectionPaused, false);
        Settings.SetUtc(SettingKeys.ProtectionPausedUntilUtc, until);
        var results = new List<ApplyResult> { ApplyWebBlocking(), ApplyAppBlocking() };
        Logs.Warn(LogCategory.Admin, $"{reason}: tạm mở bảo vệ trong {minutes:N0} phút (đến {until.ToLocalTime():HH:mm}), sau đó tự bật lại.");
        return results;
    }

    /// <summary>Đang trong giờ giải lao (tạm mở chặn mạng xã hội/game/ứng dụng) theo múi giờ phụ huynh cấu hình.</summary>
    public bool IsInBreakNow() => IsInBreakAt(DateTime.UtcNow);

    public bool IsInBreakAt(DateTime utcNow)
    {
        var config = Settings.GetTimeConfig();
        return config.BreakWindows.Count > 0 && TimePolicyEngine.IsInBreak(config, TimePolicyEngine.ToScheduleTime(config, utcNow));
    }

    /// <summary>Phạm vi chặn website (mặc định: chỉ khi tài khoản của con đang dùng máy).</summary>
    public WebBlockScope WebScope => Settings.Get(SettingKeys.WebScope) == "all" ? WebBlockScope.All : WebBlockScope.Children;

    /// <summary>Nguồn danh sách phiên Windows (thay được trong kiểm thử).</summary>
    public Func<IReadOnlyList<UserSession>> SessionProvider { get; set; } = WindowsSessions.Enumerate;

    public WebScopeDecision EvaluateWebScope()
    {
        IReadOnlyList<UserSession> sessions;
        try { sessions = SessionProvider(); }
        catch (Exception) { sessions = []; } // không xác định được → chặn (an toàn)
        return WebScopePolicy.Decide(WebScope, Settings.GetChildSids(), sessions);
    }

    public IReadOnlyList<string> ComputeExpectedDomains() =>
        IsPaused || IsInBreakNow() || !EvaluateWebScope().Block
            ? []
            : BlocklistBuilder.Build(Blocks.GetEnabledPlatformKeys(), Blocks.GetCustomDomains());

    /// <summary>Ghi danh sách tên miền hiện tại vào HOSTS.</summary>
    public ApplyResult ApplyWebBlocking()
    {
        var scope = EvaluateWebScope();
        Settings.SetBool(SettingKeys.WebScopeBlocking, scope.Block);
        var domains = ComputeExpectedDomains();
        var result = WebProvider.Apply(domains);
        if (result.Success && !IsPaused && IsInBreakNow())
            result = result with { Message = "Đang trong giờ giải lao: tạm mở chặn, sẽ tự chặn lại khi hết giờ giải lao. " + result.Message };
        else if (result.Success && !IsPaused && !scope.Block)
            result = result with
            {
                Message = $"Đã lưu. Hiện chưa chặn website vì {scope.Reason}; sẽ tự chặn ngay khi tài khoản của con đăng nhập/chuyển sang dùng máy.",
                Count = BlocklistBuilder.Build(Blocks.GetEnabledPlatformKeys(), Blocks.GetCustomDomains()).Count
            };
        if (result.Success)
        {
            Settings.SetUtc(SettingKeys.WebLastApplied, DateTime.UtcNow);
            var enabled = Blocks.GetEnabledPlatformKeys().Select(k => PlatformCatalog.Find(k)?.Name ?? k);
            var custom = Blocks.GetCustomDomains();
            Logs.Info(LogCategory.Website,
                $"{result.Message} Nền tảng: {string.Join(", ", enabled.DefaultIfEmpty("(không)"))}. Tên miền tùy chỉnh: {string.Join(", ", custom.DefaultIfEmpty("(không)"))}.");
        }
        else
        {
            Logs.Error(LogCategory.Website, result.Message);
        }
        return result;
    }

    /// <summary>Thư mục mặc định được phép chạy ở chế độ nghiêm ngặt (ứng dụng học tập/Windows cài theo từng người dùng).</summary>
    public static readonly string[] DefaultAllowedFolders =
    [
        @"%USERPROFILE%\AppData\Local\Microsoft\OneDrive",
        @"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps",
        @"%USERPROFILE%\AppData\Local\Microsoft\Teams",
        @"%USERPROFILE%\AppData\Roaming\Zoom"
    ];

    public bool AppLockerAvailable => _appLockerEnabled ?? AppLockerManager.IsSupported(Edition);

    public WindowsEditionInfo Edition => _edition ??= WindowsEdition.Detect();

    public bool StrictMode => Settings.GetBool(SettingKeys.AppLockerStrict);

    public IReadOnlyList<string> AllowedFolders =>
        Settings.Get(SettingKeys.AppLockerAllowedFolders) is { } v
            ? v.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultAllowedFolders;

    /// <summary>
    /// Áp dụng chặn ứng dụng cho tài khoản con bằng mọi cơ chế khả dụng:
    /// DisallowRun (mọi edition) + AppLocker (Enterprise/Education/Pro). Gỡ khỏi tài khoản không còn được chọn.
    /// </summary>
    public ApplyResult ApplyAppBlocking()
    {
        RecordProfilePending();
        var disallow = ApplyDisallowRun();
        ApplyAntiTamper();
        var browser = ApplyBrowserPolicies();
        if (!browser.Success) disallow = disallow with { Success = false, Message = disallow.Message + " " + browser.Message };
        if (!AppLockerAvailable) return disallow;

        var appLocker = ApplyAppLocker();
        var message = $"{disallow.Message} {appLocker.Message}";
        return new ApplyResult(disallow.Success && appLocker.Success, message, disallow.Count);
    }

    /// <summary>Quy tắc AppLocker theo SID của con. Chế độ nghiêm ngặt vẫn giữ trong giờ giải lao (khóa cài đặt vẫn hoạt động).</summary>
    public ApplyResult ApplyAppLocker()
    {
        var paused = IsPaused;
        var exes = paused || IsInBreakNow() ? [] : Apps.GetEnabledProcessNames();
        var children = Settings.GetChildSids()
            .Select(sid => new AppLockerChild(sid, ProfilePath(sid)))
            .ToList();
        if (!AppLockerManager.IsRunningAsSystem())
        {
            // Giao diện (Administrator) không ghi trực tiếp được AppLocker CSP – nhờ dịch vụ nền (SYSTEM) áp dụng
            // theo cấu hình vừa lưu trong database.
            // Task.Run: tránh deadlock khi được gọi từ luồng giao diện (có SynchronizationContext), ví dụ lúc gỡ cài đặt.
            var exePath = ServiceExePath;
            var response = Task.Run(() => PipeProtocol.SendAsync(PipeProtocol.CmdAppLocker, exePath, timeoutMs: 120_000)).GetAwaiter().GetResult();
            return response is null
                ? ApplyResult.Fail("AppLocker: dịch vụ nền không phản hồi nên chưa áp dụng được. Kiểm tra mục Bảo mật & gỡ app.")
                : new ApplyResult(response.Ok, response.Message ?? response.Error ?? "");
        }

        ApplyResult result;
        try
        {
            result = AppLockerManager.Apply(children, exes, StrictMode && !paused, AllowedFolders);
        }
        catch (Exception ex)
        {
            result = ApplyResult.Fail("Lỗi AppLocker: " + ex.Message);
        }
        Logs.Add(result.Success ? LogLevel.Info : LogLevel.Error, LogCategory.Application, result.Message);
        return result;
    }

    /// <summary>Đường dẫn ConNgoan.Service.exe để xác minh đầu bên kia của Named Pipe (mặc định cùng thư mục).</summary>
    public string ServiceExePath { get; set; } = Path.Combine(AppContext.BaseDirectory, AgentProtocol.ServiceExeName);

    private static string? ProfilePath(string sid)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") is string p ? Environment.ExpandEnvironmentVariables(p) : null;
    }

    /// <summary>Áp dụng DisallowRun cho từng tài khoản trẻ đã chọn; gỡ khỏi tài khoản không còn được chọn.</summary>
    /// <summary>Ghi nhớ tài khoản con chưa đăng nhập lần nào (chưa có hồ sơ/registry riêng) để áp dụng lại sau.</summary>
    private void RecordProfilePending()
    {
        var pending = Settings.GetChildSids().Where(sid => ProfilePath(sid) is null).ToList();
        if (pending.Count == 0) Settings.Delete(SettingKeys.ProfilePending);
        else Settings.Set(SettingKeys.ProfilePending, string.Join(";", pending));
    }

    /// <summary>
    /// Gọi bởi dịch vụ khi có người đăng nhập: tài khoản con mới tạo vừa đăng nhập lần đầu (đã có hồ sơ) →
    /// áp dụng lại chặn ứng dụng (DisallowRun cần registry của con, AppLocker cần đường dẫn hồ sơ cho thư mục được phép).
    /// </summary>
    public bool ApplyForNewProfiles()
    {
        if (Settings.Get(SettingKeys.ProfilePending) is not { Length: > 0 } raw) return false;
        var children = Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ready = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(sid => children.Contains(sid) && ProfilePath(sid) is not null)
            .ToList();
        if (ready.Count == 0) return false;
        Logs.Info(LogCategory.Application,
            $"Tài khoản của con đã đăng nhập lần đầu: {string.Join(", ", ready.Select(AccountLabel))} – áp dụng chặn ứng dụng cho tài khoản này.");
        ApplyAppBlocking(); // gồm cả chính sách trình duyệt và chống can thiệp cho tài khoản này
        return true;
    }

    private ApplyResult ApplyDisallowRun()
    {
        var exes = IsPaused || IsInBreakNow() ? [] : Apps.GetEnabledProcessNames();
        var children = Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stored = Settings.GetByPrefix(SettingKeys.DisallowRunPrefix);
        var targets = children.Union(stored.Keys.Select(k => k[SettingKeys.DisallowRunPrefix.Length..])).ToList();

        if (targets.Count == 0)
        {
            return exes.Count == 0
                ? ApplyResult.Ok("Không có ứng dụng nào bị chặn.")
                : ApplyResult.Fail("Chưa chọn tài khoản Windows của con (mục Khóa cài đặt), nên chưa thể áp dụng chặn ứng dụng.");
        }

        var errors = new List<string>();
        var pendingProfiles = new List<string>();
        var applied = 0;
        foreach (var sid in targets)
        {
            var key = SettingKeys.DisallowRunPrefix + sid;
            var previous = DisallowRunState.FromJson(stored.GetValueOrDefault(key));
            var wanted = children.Contains(sid) ? exes : [];
            if (wanted.Count > 0 && previous.OwnedValueNames.Count == 0 && !previous.FlagTouched && ProfilePath(sid) is null)
            {
                // Tài khoản mới, chưa đăng nhập lần nào: chưa có registry để ghi – dịch vụ sẽ tự áp dụng khi con đăng nhập lần đầu.
                pendingProfiles.Add(AccountLabel(sid));
                continue;
            }
            try
            {
                var state = DisallowRunProvider.Apply(sid, wanted, previous);
                if (state.OwnedValueNames.Count == 0 && !state.FlagTouched) Settings.Delete(key);
                else Settings.Set(key, state.ToJson());
                if (wanted.Count > 0) applied++;

            }
            catch (Exception ex)
            {
                errors.Add($"{AccountLabel(sid)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            var msg = "Chặn ứng dụng chưa áp dụng được cho: " + string.Join("; ", errors);
            Logs.Error(LogCategory.Application, msg);
            return ApplyResult.Fail(msg);
        }

        var ok = exes.Count == 0
            ? "Đã gỡ chặn ứng dụng khỏi các tài khoản."
            : $"Đã chặn {exes.Count} ứng dụng ({string.Join(", ", exes)}) cho {applied} tài khoản của con. Có hiệu lực ngay với lần mở tiếp theo qua Start/Desktop.";
        if (pendingProfiles.Count > 0)
            ok += $" Tài khoản {string.Join(", ", pendingProfiles)} chưa đăng nhập lần nào – sẽ tự áp dụng khi con đăng nhập lần đầu.";
        Logs.Info(LogCategory.Application, ok);
        return ApplyResult.Ok(ok, exes.Count);
    }

    /// <summary>Tình trạng thời gian hôm nay của một tài khoản con (dùng chung cho UI và dịch vụ).</summary>
    public TimeStatus GetTimeStatus(string sid, DateTime utcNow)
    {
        var config = Settings.GetTimeConfig();
        var local = TimePolicyEngine.ToScheduleTime(config, utcNow);
        var usage = Usage.Get(sid, TimePolicyEngine.DayKey(local));
        DateTime? pausedLocal = Settings.GetUtc(SettingKeys.TimePausedUntil) is { } p
            ? TimePolicyEngine.ToScheduleTime(config, p)
            : null;
        return TimePolicyEngine.Evaluate(config, local, usage.UsedSeconds, usage.ExtraSeconds, pausedLocal);
    }

    /// <summary>Phụ huynh cấp thêm thời gian cho hôm nay.</summary>
    public void GrantExtraTime(string sid, int minutes)
    {
        var config = Settings.GetTimeConfig();
        var day = TimePolicyEngine.DayKey(TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow));
        Usage.AddExtra(sid, day, minutes * 60);
        Logs.Info(LogCategory.Admin, $"Cấp thêm {minutes} phút hôm nay cho {AccountLabel(sid)}.");
    }

    /// <summary>Tạm dừng mọi giới hạn thời gian đến hết ngày (theo múi giờ cấu hình); null = bỏ tạm dừng.</summary>
    public void PauseTimeLimitsToday(bool pause)
    {
        if (!pause)
        {
            Settings.Delete(SettingKeys.TimePausedUntil);
            Logs.Info(LogCategory.Admin, "Bỏ tạm dừng giới hạn thời gian.");
            return;
        }
        var config = Settings.GetTimeConfig();
        var local = TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow);
        var endLocal = local.Date.AddDays(1);
        var offset = local - DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        Settings.SetUtc(SettingKeys.TimePausedUntil, DateTime.SpecifyKind(endLocal - offset, DateTimeKind.Utc));
        Logs.Info(LogCategory.Admin, "Tạm dừng giới hạn thời gian đến hết hôm nay.");
    }

    public static string AccountName(string sid) => AccountLabel(sid);

    /// <summary>Con gửi yêu cầu xin thêm giờ (qua Agent). Kiểm tra hợp lệ và giới hạn số lần.</summary>
    public ApplyResult SubmitExtraRequest(string sid, int minutes, string? reason)
    {
        if (!Settings.GetChildSids().Contains(sid, StringComparer.OrdinalIgnoreCase))
            return ApplyResult.Fail("Tài khoản này không thuộc diện quản lý thời gian.");
        if (!ExtraRequestRepository.AllowedMinutes.Contains(minutes))
            return ApplyResult.Fail("Chỉ được xin 15, 30 hoặc 60 phút.");
        if (Requests.Latest(sid) is { Status: ExtraRequestRepository.Pending })
            return ApplyResult.Fail("Yêu cầu trước vẫn đang chờ phụ huynh duyệt.");
        var config = Settings.GetTimeConfig();
        var local = TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow);
        var dayStartUtc = DateTime.UtcNow - local.TimeOfDay;
        if (Requests.CountSince(sid, dayStartUtc) >= ExtraRequestRepository.MaxPerDay)
            return ApplyResult.Fail($"Hôm nay đã xin {ExtraRequestRepository.MaxPerDay} lần, không thể xin thêm.");

        var clean = new string((reason ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        Requests.Create(sid, minutes, clean);
        Logs.Info(LogCategory.Time, $"{AccountLabel(sid)} xin thêm {minutes} phút" + (clean.Length > 0 ? $": \"{clean}\"." : "."));
        return ApplyResult.Ok("Đã gửi yêu cầu, chờ phụ huynh duyệt.");
    }

    /// <summary>Phụ huynh duyệt (cấp thêm giờ) hoặc từ chối yêu cầu.</summary>
    /// <param name="minutes">Số phút cấp (phụ huynh có thể cấp khác số con xin, 5–240); null = đúng số con xin.</param>
    public ApplyResult DecideExtraRequest(long id, bool approve, int? minutes = null)
    {
        var request = Requests.Get(id);
        if (request is null || !Requests.Decide(id, approve))
            return ApplyResult.Fail("Yêu cầu này đã được xử lý trước đó.");
        var granted = Math.Clamp(minutes ?? request.Minutes, 5, 240);
        if (approve) GrantExtraTime(request.Sid, granted);
        else Logs.Info(LogCategory.Admin, $"Từ chối yêu cầu thêm {request.Minutes} phút của {AccountLabel(request.Sid)}.");
        return ApplyResult.Ok(approve
            ? $"Đã duyệt: cấp thêm {granted} phút cho {AccountLabel(request.Sid)}."
            : $"Đã từ chối yêu cầu của {AccountLabel(request.Sid)}.");
    }

    /// <summary>Không tính thời gian khi con không thao tác lâu hơn ngưỡng (mặc định bật, 5 phút).</summary>
    public bool IdleExclusionEnabled => Settings.GetBool(SettingKeys.IdleExclusion, fallback: true);

    public int IdleThresholdSeconds => Math.Clamp(Settings.GetInt(SettingKeys.IdleThresholdSeconds, 300), 5, 3600);

    /// <summary>Dịch vụ nền gọi định kỳ hoặc khi HOSTS thay đổi: khôi phục nếu có người sửa/xóa khối chặn.</summary>
    public ApplyResult? VerifyAndRepairWeb()
    {
        var scope = EvaluateWebScope();
        var scopeChanged = scope.Block != Settings.GetBool(SettingKeys.WebScopeBlocking, fallback: true);
        if (scopeChanged) Settings.SetBool(SettingKeys.WebScopeBlocking, scope.Block);

        var expected = ComputeExpectedDomains();
        if (WebProvider.IsInSync(expected)) return null;
        // Vào/ra giờ giải lao hoặc đổi người dùng cũng làm danh sách mong muốn thay đổi – không phải bị can thiệp.
        var breakTransition = IsInBreakNow() != Settings.GetBool(SettingKeys.BreakApplied);
        var result = WebProvider.Apply(expected);
        if (result.Success && scopeChanged)
            Logs.Info(LogCategory.Website, scope.Block
                ? $"Bật chặn website: {scope.Reason}."
                : $"Tạm mở chặn website: {scope.Reason}.");
        else if (result.Success && breakTransition)
            Logs.Info(LogCategory.Time, "Cập nhật chặn website theo giờ giải lao.");
        else if (result.Success)
            Logs.Warn(LogCategory.Service, "Phát hiện HOSTS bị thay đổi ngoài Con Ngoan, đã khôi phục cấu hình chặn website.");
        else
            Logs.Error(LogCategory.Service, "Không khôi phục được HOSTS: " + result.Message);
        return result;
    }

    public BrowserPolicyService.Options BrowserOptions => new(
        Settings.GetBool(SettingKeys.BrowserDisableDoH, fallback: true),
        Settings.GetBool(SettingKeys.BrowserBlockExtensions, fallback: false),
        Settings.GetBool(SettingKeys.BrowserSafeSearch, fallback: true),
        Settings.GetBool(SettingKeys.BrowserBlockIncognito, fallback: true));

    /// <summary>
    /// Chính sách trình duyệt CHỈ cho tài khoản con (registry riêng của con). Tài khoản không còn là con, hoặc khi tạm tắt bảo vệ,
    /// được hoàn tác về giá trị gốc. Tài khoản chưa có hồ sơ sẽ được áp dụng ở lần đăng nhập đầu (ApplyForNewProfiles).
    /// Giờ giải lao không tắt chính sách này (chỉ là SafeSearch/DoH/ẩn danh, không chặn trang).
    /// </summary>
    public ApplyResult ApplyBrowserPolicies()
    {
        var legacy = BrowserPolicy.RestoreLegacyMachinePolicies();
        if (legacy > 0) Logs.Info(LogCategory.Website, $"Đã gỡ {legacy} chính sách trình duyệt cấp máy (áp cho cả phụ huynh) do bản 1.8.0 thử nghiệm để lại.");

        var children = Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = IsPaused ? BrowserPolicyService.Options.None : BrowserOptions;
        var errors = new List<string>();
        var applied = 0;
        foreach (var sid in children.Union(BrowserPolicy.UsersWithChanges(), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (children.Contains(sid) && options != BrowserPolicyService.Options.None)
                {
                    if (ProfilePath(sid) is null) continue;
                    BrowserPolicy.ApplyForUser(sid, options);
                    applied++;
                }
                else if (ProfilePath(sid) is not null)
                {
                    BrowserPolicy.RestoreUser(sid);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                errors.Add($"{AccountLabel(sid)}: {ex.Message}");
            }
        }
        if (errors.Count > 0)
        {
            var msg = "Chính sách trình duyệt chưa áp dụng được cho: " + string.Join("; ", errors);
            Logs.Error(LogCategory.Website, msg);
            return ApplyResult.Fail(msg);
        }
        return ApplyResult.Ok(applied == 0
            ? "Không có chính sách trình duyệt nào cần áp dụng."
            : $"Đã áp dụng chính sách trình duyệt cho {applied} tài khoản của con (không ảnh hưởng tài khoản phụ huynh).", applied);
    }

    /// <summary>
    /// Khóa Task Manager / Registry Editor / Command Prompt cho tài khoản con (registry riêng của con).
    /// Theo dõi danh sách tài khoản đã khóa để gỡ đúng khi bỏ chọn tài khoản, tạm tắt bảo vệ hoặc gỡ cài đặt.
    /// </summary>
    public void ApplyAntiTamper()
    {
        var children = Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabled = !IsPaused && Settings.GetBool(SettingKeys.SecurityAntiTamper, fallback: true);
        var tracked = (Settings.Get(SettingKeys.AntiTamperSids) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in children.Union(tracked, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (ProfilePath(sid) is null) continue;
            try
            {
                if (enabled && children.Contains(sid))
                {
                    AntiTamperService.Apply(sid);
                    tracked.Add(sid);
                }
                else if (tracked.Contains(sid))
                {
                    AntiTamperService.Remove(sid);
                    tracked.Remove(sid);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                Logs.Error(LogCategory.Application, $"Chống can thiệp: lỗi với {AccountLabel(sid)}: {ex.Message}");
            }
        }
        if (tracked.Count == 0) Settings.Delete(SettingKeys.AntiTamperSids);
        else Settings.Set(SettingKeys.AntiTamperSids, string.Join(";", tracked));
    }

    /// <summary>Tạm tắt toàn bộ bảo vệ (gỡ HOSTS + DisallowRun + chính sách trình duyệt). Cấu hình vẫn được giữ để bật lại.</summary>
    public IReadOnlyList<ApplyResult> PauseProtection()
    {
        Settings.Delete(SettingKeys.ProtectionPausedUntilUtc);
        Settings.SetBool(SettingKeys.ProtectionPaused, true);
        var results = new List<ApplyResult> { ApplyWebBlocking(), ApplyAppBlocking() };
        Logs.Warn(LogCategory.Admin, "Phụ huynh đã tạm tắt bảo vệ.");
        return results;
    }

    public IReadOnlyList<ApplyResult> ResumeProtection()
    {
        Settings.Delete(SettingKeys.ProtectionPausedUntilUtc);
        Settings.SetBool(SettingKeys.ProtectionPaused, false);
        var results = new List<ApplyResult> { ApplyWebBlocking(), ApplyAppBlocking() };
        Logs.Info(LogCategory.Admin, "Phụ huynh đã bật lại bảo vệ.");
        return results;
    }

    /// <summary>Dùng khi gỡ cài đặt: hoàn tác mọi thay đổi hệ thống do Con Ngoan tạo.</summary>
    public IReadOnlyList<string> UninstallCleanup()
    {
        var report = new List<string>();
        // Tạm tắt trước để dịch vụ nền (nếu còn chạy) không áp dụng lại khối HOSTS vừa gỡ.
        // Giữ nguyên cấu hình (tài khoản con, danh sách chặn) cho trường hợp phụ huynh giữ dữ liệu và cài lại.
        Settings.SetBool(SettingKeys.ProtectionPaused, true);
        Settings.SetBool(SettingKeys.Uninstalled, true);
        var web = WebProvider.RemoveAll();
        report.Add("HOSTS: " + web.Message);

        var apps = ApplyAppBlocking(); // đang tạm tắt → gỡ DisallowRun, AppLocker, chính sách trình duyệt, chống can thiệp
        report.Add("Chính sách trình duyệt: " + (BrowserPolicy.UsersWithChanges().Count == 0 ? "đã khôi phục." : "còn tài khoản chưa khôi phục được."));
        report.Add("Ứng dụng: " + apps.Message);
        if (AppLockerAvailable && !apps.Success && !AppLockerManager.IsRunningAsSystem())
        {
            // Dịch vụ nền đã dừng – gỡ AppLocker bằng tác vụ SYSTEM tạm thời.
            var fallback = AppLockerManager.RemoveViaSystemTask(ServiceExePath);
            report.Add("AppLocker (dự phòng): " + fallback.Message);
        }

        try
        {
            InstallPolicy.RestoreAll();
            report.Add("Chính sách cài đặt: đã khôi phục giá trị gốc.");
        }
        catch (Exception ex)
        {
            report.Add("Chính sách cài đặt: lỗi khôi phục - " + ex.Message);
        }
        Logs.Info(LogCategory.System, "Gỡ cài đặt: " + string.Join(" | ", report));
        return report;
    }

    private static string AccountLabel(string sid)
    {
        try
        {
            return new System.Security.Principal.SecurityIdentifier(sid)
                .Translate(typeof(System.Security.Principal.NTAccount)).Value;
        }
        catch (Exception)
        {
            return sid;
        }
    }
}
