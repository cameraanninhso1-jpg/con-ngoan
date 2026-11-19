using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Services;
using CaoMinhComputer.UI.ViewModels;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class BreakPage : UserControl
{
    private static readonly string[] Labels = ["Nạp pin", "Thư giãn", "Chia sẻ", "Khám phá", "Giải trí"];
    private readonly ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private readonly ObservableCollection<WindowRowViewModel> _rows = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _loading = true;

    public BreakPage()
    {
        InitializeComponent();
        Banner.DataContext = _status;
        BreakList.ItemsSource = _rows;
        Load();
        _clock.Tick += (_, _) => RefreshState();
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
    }

    private void Load()
    {
        _loading = true;
        var config = _c.Settings.GetTimeConfig();
        _rows.Clear();
        for (var i = 0; i < TimeConfig.MaxBreakWindows; i++)
            _rows.Add(new WindowRowViewModel(Labels[i], i < config.BreakWindows.Count ? config.BreakWindows[i] : null, MarkDirty));
        CloseBrowsersCheck.IsChecked = config.CloseBrowsersAfterBreak;
        _loading = false;
        DirtyPill.Visibility = Visibility.Collapsed;
        RefreshState();
    }

    private void MarkDirty()
    {
        if (!_loading) DirtyPill.Visibility = Visibility.Visible;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void RefreshState()
    {
        var config = _c.Settings.GetTimeConfig();
        var local = TimePolicyEngine.ToScheduleTime(config, DateTime.UtcNow);
        var hasWindows = config.BreakWindows.Any(w => w.IsSet);
        var inBreak = TimePolicyEngine.IsInBreak(config, local, out var left);
        ClockText.Text = $"Giờ trên máy tính: {local:HH:mm}" + (inBreak ? $" · còn {TimePolicyEngine.FormatDuration(left)} giải lao" : "");

        var (title, color, bg, border) = _c.IsPaused
            ? ("Bảo vệ đang tạm tắt – không chặn gì", "#C77A00", "#FFF6DF", "#F7CF6E")
            : inBreak
                ? ("Đang trong giờ giải lao – tạm mở mạng xã hội & game", "#C77A00", "#FFF6DF", "#F7CF6E")
                : hasWindows
                    ? ("Đang chặn theo cài đặt (ngoài giờ giải lao)", "#1D9A57", "#E4F6EC", "#BFE8D0")
                    : ("Chưa đặt giờ giải lao – luôn chặn theo cài đặt", "#1D9A57", "#E4F6EC", "#BFE8D0");
        var conv = new BrushConverter();
        StateTitle.Text = title;
        StateTitle.Foreground = (Brush)conv.ConvertFromString(color)!;
        StateDot.Fill = (Brush)conv.ConvertFromString(color)!;
        StateCard.Background = (Brush)conv.ConvertFromString(bg)!;
        StateCard.BorderBrush = (Brush)conv.ConvertFromString(border)!;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var partial = _rows.Where(r => string.IsNullOrWhiteSpace(r.From) != string.IsNullOrWhiteSpace(r.To)).Select(r => r.Label).ToList();
        var config = _c.Settings.GetTimeConfig();
        config.BreakWindows = _rows.Select(r => r.ToModel()).Where(w => w.IsSet).ToList();
        config.CloseBrowsersAfterBreak = CloseBrowsersCheck.IsChecked == true;
        config.TimeZoneId = TimeZoneInfo.Local.Id;

        var errors = TimePolicyEngine.Validate(config).Where(m => m.StartsWith("Giờ giải lao", StringComparison.Ordinal)).ToList();
        errors.AddRange(partial.Select(l => $"{l}: cần nhập cả giờ bắt đầu và kết thúc."));
        if (errors.Count > 0)
        {
            _status.Show("Chưa lưu – khung giờ chưa hợp lệ:\n• " + string.Join("\n• ", errors), true);
            return;
        }

        _c.Settings.SaveTimeConfig(config);
        _c.Logs.Info(LogCategory.Settings,
            $"Lưu giờ giải lao: {string.Join(", ", config.BreakWindows.Select(w => $"{w.From}–{w.To}").DefaultIfEmpty("không có"))}; " +
            $"tự đóng trình duyệt: {(config.CloseBrowsersAfterBreak ? "bật" : "tắt")}.");

        // Dịch vụ nền tự chuyển chế độ chặn khi vào/ra giờ giải lao; yêu cầu áp dụng ngay.
        var service = await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        _status.Show(service is null
            ? "Đã lưu khung giờ. Cảnh báo: dịch vụ nền không phản hồi nên chưa tự chuyển chế độ chặn theo giờ giải lao."
            : "Đã lưu khung giờ giải lao. Dịch vụ nền sẽ tự mở/chặn lại đúng giờ.", service is null);
        Load();
    }
}
