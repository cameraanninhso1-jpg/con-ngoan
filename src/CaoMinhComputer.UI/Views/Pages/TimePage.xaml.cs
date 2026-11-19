using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using CaoMinhComputer.UI.ViewModels;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class TimePage : UserControl
{
    internal static readonly string[] DayNames = ["Chủ nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];
    private static readonly int[] DisplayOrder = [1, 2, 3, 4, 5, 6, 0];

    private readonly ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly ObservableCollection<DayRowViewModel> _days = [];
    private readonly ObservableCollection<WindowRowViewModel> _rests = [];
    private TimeConfig _config = new();
    private bool _loading = true;

    public TimePage()
    {
        InitializeComponent();
        Banner.DataContext = _status;
        DayList.ItemsSource = _days;
        RestList.ItemsSource = _rests;
        Load();
        _refresh.Tick += async (_, _) => await RefreshLiveAsync();
        Loaded += async (_, _) =>
        {
            await RefreshLiveAsync();
            _refresh.Start();
        };
        Unloaded += (_, _) => _refresh.Stop();
    }

    private void Load()
    {
        _loading = true;
        _config = _c.Settings.GetTimeConfig();
        EnabledToggle.IsChecked = _config.Enabled;
        TimeZoneText.Text = $"Theo múi giờ {TimeZoneInfo.FindSystemTimeZoneById(SafeTz(_config.TimeZoneId)).DisplayName} · Giờ trên máy: {DateTime.Now:HH:mm}";

        _days.Clear();
        foreach (var i in DisplayOrder) _days.Add(new DayRowViewModel(i, DayNames[i], _config.Days[i], MarkDirty));

        _rests.Clear();
        for (var i = 0; i < TimeConfig.MaxRestWindows; i++)
            _rests.Add(new WindowRowViewModel($"Nghỉ {i + 1}", i < _config.RestWindows.Count ? _config.RestWindows[i] : null, MarkDirty));

        (_config.Action == LimitAction.Logoff ? ActionLogoff : ActionLock).IsChecked = true;
        IdleCheck.IsChecked = _c.IdleExclusionEnabled;
        GraceCombo.SelectedItem = GraceCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _config.GraceMinutes.ToString())
                                  ?? GraceCombo.Items[1];
        _loading = false;
        DirtyPill.Visibility = Visibility.Collapsed;
        LoadReport();
    }

    private static string SafeTz(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return id; }
        catch (Exception) { return TimeZoneInfo.Local.Id; }
    }

    private void MarkDirty()
    {
        if (!_loading) DirtyPill.Visibility = Visibility.Visible;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void OnGraceChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    private void OnCopyWeekdays(object sender, RoutedEventArgs e)
    {
        var monday = _days.First(d => d.DayIndex == 1);
        foreach (var d in _days.Where(d => d.DayIndex is >= 2 and <= 5)) d.CopyFrom(monday);
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        var monday = _days.First(d => d.DayIndex == 1);
        foreach (var d in _days.Where(d => d.DayIndex != 1)) d.CopyFrom(monday);
    }

    /// <summary>Cập nhật tình trạng hôm nay (từ database + trạng thái phiên trực tiếp qua Named Pipe).</summary>
    private async Task RefreshLiveAsync()
    {
        var pending = _c.Requests.GetPending();
        foreach (var r in pending) r.AccountName = ProtectionCoordinator.AccountName(r.Sid);
        RequestList.ItemsSource = pending;
        RequestsPanel.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var children = _c.Settings.GetChildSids();
        NoChildText.Visibility = children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var paused = _c.Settings.GetUtc(SettingKeys.TimePausedUntil) is { } p && p > DateTime.UtcNow;
        PauseButton.Content = paused ? "Bỏ tạm dừng giới hạn" : "Tạm dừng giới hạn hôm nay";
        PauseButton.Visibility = children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var status = await AppServices.SendToServiceAsync(PipeProtocol.CmdStatus);
        var utc = DateTime.UtcNow;
        var items = new List<ChildTimeViewModel>();
        foreach (var sid in children)
        {
            var s = _c.GetTimeStatus(sid, utc);
            var session = status?.Sessions?.FirstOrDefault(x => x.Sid == sid);
            var presence = status is null
                ? "(không kết nối được dịch vụ nền)"
                : session is null ? "· không đăng nhập"
                : session.IsLocked || !session.IsActive ? "· đang khóa màn hình"
                : "· đang dùng máy";
            items.Add(new ChildTimeViewModel
            {
                Sid = sid,
                Name = ProtectionCoordinator.AccountName(sid),
                Description = s.Description,
                UsedMinutes = s.UsedSeconds / 60.0,
                LimitMinutes = (s.LimitSeconds ?? 0) / 60.0,
                UsedText = s.LimitSeconds is { } l
                    ? $"Đã dùng {s.UsedSeconds / 60} phút / {l / 60} phút hôm nay"
                    : $"Đã dùng {s.UsedSeconds / 60} phút hôm nay",
                Allowed = s.Allowed,
                PresenceText = presence
            });
        }
        ChildList.ItemsSource = items;
    }

    private void LoadReport()
    {
        var config = _c.Settings.GetTimeConfig();
        var today = TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow).Date;
        var from = today.AddDays(-6);
        var rows = _c.Usage.GetRange(TimePolicyEngine.DayKey(from), TimePolicyEngine.DayKey(today));
        var children = _c.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totals = Enumerable.Range(0, 7).Select(i =>
        {
            var day = from.AddDays(i);
            var key = TimePolicyEngine.DayKey(day);
            var seconds = rows.Where(r => r.Day == key && (children.Count == 0 || children.Contains(r.Sid))).Sum(r => r.UsedSeconds);
            return (Day: day, Minutes: seconds / 60);
        }).ToList();
        var max = Math.Max(60, totals.Max(t => t.Minutes));
        BarList.ItemsSource = totals.Select(t => new DayBarViewModel(
            t.Day == today ? "Hôm nay" : $"{DayNames[(int)t.Day.DayOfWeek]} {t.Day:dd/MM}",
            t.Minutes >= 60 ? $"{t.Minutes / 60}g{t.Minutes % 60:00}" : $"{t.Minutes}p",
            Math.Max(3, 120.0 * t.Minutes / max),
            t.Day == today)).ToList();
        ReportTitle.Text = $"Tổng thời gian dùng máy của các tài khoản con: {totals.Sum(t => t.Minutes) / 60} giờ {totals.Sum(t => t.Minutes) % 60} phút trong 7 ngày.";

        var sessions = _c.Usage.RecentSessions(30);
        foreach (var s in sessions) s.AccountName = ProtectionCoordinator.AccountName(s.Sid);
        SessionGrid.ItemsSource = sessions;
    }

    private async void OnGrant(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag, DataContext: ChildTimeViewModel child } || !int.TryParse(tag, out var minutes)) return;
        if (!DialogWindow.ReAuthenticate(Window.GetWindow(this), $"cấp thêm {minutes} phút cho {child.Name}")) return;
        _c.GrantExtraTime(child.Sid, minutes);
        var service = await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        _status.Show($"Đã cấp thêm {minutes} phút hôm nay cho {child.Name}." +
                     (service is null ? " (Dịch vụ nền chưa phản hồi – sẽ áp dụng ở lần kiểm tra kế tiếp.)" : ""), false);
        await RefreshLiveAsync();
    }

    private async void OnDecide(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action, DataContext: ExtraRequest request }) return;
        var approve = action == "approve";
        if (approve && !DialogWindow.ReAuthenticate(Window.GetWindow(this), $"đồng ý thêm {request.Minutes} phút cho {request.AccountName}")) return;
        var result = _c.DecideExtraRequest(request.Id, approve);
        await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        _status.Show(result);
        await RefreshLiveAsync();
    }

    private async void OnTogglePause(object sender, RoutedEventArgs e)
    {
        var paused = _c.Settings.GetUtc(SettingKeys.TimePausedUntil) is { } p && p > DateTime.UtcNow;
        if (!paused && !DialogWindow.ReAuthenticate(Window.GetWindow(this), "tạm dừng giới hạn thời gian đến hết hôm nay")) return;
        _c.PauseTimeLimitsToday(!paused);
        await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        _status.Show(paused ? "Đã bỏ tạm dừng – giới hạn thời gian có hiệu lực trở lại." : "Đã tạm dừng giới hạn thời gian đến hết hôm nay.", false);
        await RefreshLiveAsync();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var current = _c.Settings.GetTimeConfig(); // giữ cấu hình giờ giải lao do trang khác quản lý
        var config = new TimeConfig
        {
            Enabled = EnabledToggle.IsChecked == true,
            Days = new DaySchedule[7],
            RestWindows = _rests.Select(r => r.ToModel()).Where(w => w.IsSet).ToList(),
            BreakWindows = current.BreakWindows,
            CloseBrowsersAfterBreak = current.CloseBrowsersAfterBreak,
            Action = ActionLogoff.IsChecked == true ? LimitAction.Logoff : LimitAction.Lock,
            GraceMinutes = GraceCombo.SelectedItem is ComboBoxItem { Tag: string g } && int.TryParse(g, out var grace) ? grace : 2,
            TimeZoneId = TimeZoneInfo.Local.Id
        };
        foreach (var d in _days)
        {
            var model = d.ToModel();
            model.From = TimeOfDayParser.Normalize(model.From) ?? model.From;
            model.To = TimeOfDayParser.Normalize(model.To) ?? model.To;
            config.Days[d.DayIndex] = model;
        }

        // Ô giờ nghỉ chỉ nhập một nửa vẫn phải báo lỗi.
        var partial = _rests.Where(r => string.IsNullOrWhiteSpace(r.From) != string.IsNullOrWhiteSpace(r.To)).Select(r => r.Label).ToList();
        var errors = TimePolicyEngine.Validate(config).ToList();
        errors.AddRange(partial.Select(l => $"{l}: cần nhập cả giờ bắt đầu và kết thúc."));
        if (errors.Count > 0)
        {
            _status.Show("Chưa lưu – cấu hình chưa hợp lệ:\n• " + string.Join("\n• ", errors), true);
            return;
        }

        _c.Settings.SaveTimeConfig(config);
        _c.Settings.SetBool(SettingKeys.IdleExclusion, IdleCheck.IsChecked == true);
        var summary = string.Join("; ", DisplayOrder.Select(i =>
        {
            var d = config.Days[i];
            return !d.Allowed ? $"{DayNames[i]}: không dùng" : $"{DayNames[i]}: {(d.LimitMinutes > 0 ? d.LimitMinutes + " phút" : "không giới hạn")} {d.From}–{d.To}";
        }));
        _c.Logs.Info(LogCategory.Settings,
            $"Lưu quản lý thời gian ({(config.Enabled ? "bật" : "tắt")}): {summary}. Giờ nghỉ: " +
            $"{string.Join(", ", config.RestWindows.Select(w => $"{w.From}–{w.To}").DefaultIfEmpty("không"))}. " +
            $"Hết giờ: {(config.Action == LimitAction.Lock ? "khóa máy" : "đăng xuất")}, ân hạn {config.GraceMinutes} phút. " +
            $"Không tính thời gian rời máy: {(IdleCheck.IsChecked == true ? "bật" : "tắt")}.");

        var service = await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        var message = config.Enabled ? "Đã lưu và áp dụng giới hạn thời gian." : "Đã lưu. Giới hạn thời gian đang TẮT.";
        if (service is null) message += " Cảnh báo: dịch vụ nền không phản hồi nên giới hạn chưa được cưỡng chế – kiểm tra mục Bảo mật & gỡ app.";
        if (_c.Settings.GetChildSids().Count == 0) message += " Chưa chọn tài khoản của con nên chưa có ai bị giới hạn.";
        _status.Show(message, service is null);
        Load();
        await RefreshLiveAsync();
    }
}
