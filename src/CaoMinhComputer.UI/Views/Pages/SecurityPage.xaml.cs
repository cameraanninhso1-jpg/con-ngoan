using System.Diagnostics;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.UI.ViewModels;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class SecurityPage : UserControl
{
    private readonly Data.Services.ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private readonly ShellView _shell;

    public SecurityPage(ShellView shell)
    {
        InitializeComponent();
        _shell = shell;
        Banner.DataContext = _status;
        Loaded += (_, _) => Refresh();
    }

    private Window? Owner => Window.GetWindow(this);

    private void Refresh()
    {
        var state = AppServices.GetServiceState();
        var heartbeat = _c.Settings.GetUtc(SettingKeys.ServiceHeartbeat);
        var hb = heartbeat is { } h ? $" Lần kiểm tra gần nhất: {h.ToLocalTime():dd/MM/yyyy HH:mm:ss}." : "";
        switch (state)
        {
            case AppServices.ServiceState.Running:
                SetService("Dịch vụ bảo vệ đang chạy", "ConNgoanService tự khôi phục HOSTS khi bị sửa và đóng ứng dụng bị chặn (nếu bật)." + hb, true);
                break;
            case AppServices.ServiceState.Stopped:
                SetService("Dịch vụ bảo vệ đã dừng", "Các chặn đã áp dụng vẫn còn hiệu lực, nhưng không tự phục hồi." + hb, false);
                break;
            case AppServices.ServiceState.NotInstalled:
                SetService("Dịch vụ bảo vệ chưa được cài", "Hãy cài bằng bộ cài ConNgoan-Setup-x64.exe để có dịch vụ nền.", false);
                break;
            default:
                SetService("Dịch vụ đang chuyển trạng thái", "Vui lòng chờ vài giây rồi kiểm tra lại.", false);
                break;
        }
        StartServiceButton.IsEnabled = state == AppServices.ServiceState.Stopped;
        StopServiceButton.IsEnabled = state == AppServices.ServiceState.Running;
        RestartServiceButton.IsEnabled = state == AppServices.ServiceState.Running;

        var paused = _c.IsPaused;
        ProtectionTitle.Text = paused ? "Bảo vệ đang TẠM TẮT" : "Bảo vệ đang bật";
        ProtectionDetail.Text = paused
            ? "Không chặn website/ứng dụng. Cấu hình vẫn được giữ, bấm bật lại để áp dụng."
            : "Đang áp dụng chặn website và ứng dụng theo cấu hình. Tạm tắt cần xác thực quản trị.";
        ProtectionButton.Content = paused ? "Bật lại bảo vệ" : "Tạm tắt bảo vệ";
        ProtectionButton.Style = (Style)FindResource(paused ? "PrimaryButton" : "DangerButton");

        var tgEnabled = _c.Settings.GetBool(SettingKeys.TelegramEnabled);
        TelegramEnabledBox.IsChecked = tgEnabled;
        TelegramTokenBox.Text = _c.Settings.Get(SettingKeys.TelegramBotToken) ?? "";
        TelegramChatIdBox.Text = _c.Settings.Get(SettingKeys.TelegramChatId) ?? "";
        TelegramNotifyBox.IsChecked = _c.Settings.GetBool(SettingKeys.TelegramNotifyExtraTime, fallback: true);
        TelegramDailyReportBox.IsChecked = _c.Settings.GetBool(SettingKeys.TelegramDailyReport, fallback: true);
        TelegramDailyTimeBox.Text = _c.Settings.Get(SettingKeys.TelegramDailyReportTime) ?? "21:00";
        TelegramConfigPanel.Visibility = tgEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Nạp trạng thái mà không kích hoạt OnAntiTamperChanged (tránh lưu nhầm giá trị của ô chưa kịp nạp).
        _loadingToggles = true;
        try
        {
            AntiTamperBox.IsChecked = _c.Settings.GetBool(SettingKeys.SecurityAntiTamper, fallback: true);
            RealtimeBox.IsChecked = _c.Settings.GetBool(SettingKeys.RealtimeProcessBlocking, fallback: true);
            SuperviseAgentBox.IsChecked = _c.Settings.GetBool(SettingKeys.SecuritySuperviseAgent, fallback: true);
        }
        finally
        {
            _loadingToggles = false;
        }

        _shell.RefreshStatus();
    }

    private void SetService(string title, string detail, bool ok)
    {
        ServiceTitle.Text = title;
        ServiceDetail.Text = detail;
        ServiceIconBg.Background = (Brush)FindResource(ok ? "SuccessSoftBrush" : "DangerSoftBrush");
        ServiceIcon.Foreground = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
    }

    private async Task ControlServiceAsync(string verb, Action<ServiceController> action, ServiceControllerStatus target)
    {
        IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(AppPaths.ServiceName);
                action(sc);
                sc.WaitForStatus(target, TimeSpan.FromSeconds(30));
            });
            _c.Logs.Info(LogCategory.Service, $"Phụ huynh {verb} dịch vụ bảo vệ.");
            _status.Show($"Đã {verb} dịch vụ bảo vệ.", false);
        }
        catch (Exception ex)
        {
            _c.Logs.Error(LogCategory.Service, $"Không {verb} được dịch vụ: {ex.Message}");
            _status.Show($"Không {verb} được dịch vụ: {ex.Message}", true);
        }
        finally
        {
            IsEnabled = true;
            Refresh();
        }
    }

    private async void OnStartService(object sender, RoutedEventArgs e) =>
        await ControlServiceAsync("khởi động", sc => sc.Start(), ServiceControllerStatus.Running);

    private async void OnStopService(object sender, RoutedEventArgs e)
    {
        if (!DialogWindow.ReAuthenticate(Owner, "dừng dịch vụ bảo vệ")) return;
        await ControlServiceAsync("dừng", sc => sc.Stop(), ServiceControllerStatus.Stopped);
    }

    private async void OnRestartService(object sender, RoutedEventArgs e)
    {
        await ControlServiceAsync("khởi động lại", sc =>
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            sc.Start();
        }, ServiceControllerStatus.Running);
    }

    private async void OnToggleProtection(object sender, RoutedEventArgs e)
    {
        var paused = _c.IsPaused;
        if (!paused && !DialogWindow.ReAuthenticate(Owner, "tạm tắt toàn bộ bảo vệ")) return;
        IsEnabled = false;
        try
        {
            var results = await Task.Run(() => paused ? _c.ResumeProtection() : _c.PauseProtection());
            var failed = results.Any(r => !r.Success);
            _status.Show((paused ? "Đã bật lại bảo vệ.\n" : "Đã tạm tắt bảo vệ.\n") + string.Join("\n", results.Select(r => r.Message)), failed);
        }
        finally
        {
            IsEnabled = true;
            Refresh();
        }
    }

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        try
        {
            var lines = await Task.Run(() =>
            {
                var list = new List<string>();
                list.Add(_c.Database.CheckIntegrity(out var detail)
                    ? "✔ Database toàn vẹn (" + _c.Database.FilePath + ")."
                    : "✘ Database lỗi toàn vẹn: " + detail);

                var expected = _c.ComputeExpectedDomains();
                if (_c.WebProvider.IsInSync(expected))
                {
                    list.Add($"✔ HOSTS khớp cấu hình ({expected.Count} tên miền).");
                }
                else
                {
                    var r = _c.ApplyWebBlocking();
                    list.Add(r.Success ? "⚠ HOSTS không khớp cấu hình – đã sửa lại. " + r.Message : "✘ HOSTS không khớp và không sửa được: " + r.Message);
                }

                var last = _c.Settings.GetUtc(SettingKeys.WebLastApplied);
                list.Add(last is { } t ? $"Lần áp dụng chặn website gần nhất: {t.ToLocalTime():dd/MM/yyyy HH:mm:ss}." : "Chưa áp dụng chặn website lần nào.");
                list.Add("Số dòng nhật ký: " + _c.Logs.Count());
                return list;
            });
            CheckDetail.Text = string.Join("\n", lines);
            CheckDetail.Visibility = Visibility.Visible;
            var bad = lines.Any(l => l.StartsWith('✘'));
            _c.Logs.Add(bad ? LogLevel.Error : LogLevel.Info, LogCategory.System, "Kiểm tra toàn vẹn: " + string.Join(" | ", lines));
            _status.Show(bad ? "Phát hiện lỗi khi kiểm tra – xem chi tiết bên dưới." : "Kiểm tra hoàn tất.", bad);
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private bool _loadingToggles;

    private void OnAntiTamperChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loadingToggles) return;
        var antiTamper = AntiTamperBox.IsChecked == true;
        var supervise = SuperviseAgentBox.IsChecked == true;
        var realtime = RealtimeBox.IsChecked == true;

        _c.Settings.SetBool(SettingKeys.SecurityAntiTamper, antiTamper);
        _c.Settings.SetBool(SettingKeys.SecuritySuperviseAgent, supervise);
        _c.Settings.SetBool(SettingKeys.RealtimeProcessBlocking, realtime);
        _c.Logs.Info(LogCategory.Settings,
            $"Chống can thiệp: khóa Task Manager/Regedit/CMD {(antiTamper ? "bật" : "tắt")}, tự mở lại tiện ích {(supervise ? "bật" : "tắt")}, đóng ứng dụng ngay khi mở {(realtime ? "bật" : "tắt")}.");

        Task.Run(() =>
        {
            _c.ApplyAppBlocking();
        });
        _status.Show("Đã cập nhật chính sách chống can thiệp hệ thống.", false);
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        var uninstaller = Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").OrderBy(f => f).FirstOrDefault();
        if (uninstaller is null)
        {
            _status.Show("Không tìm thấy bộ gỡ cài đặt (bản đang chạy không được cài bằng ConNgoan-Setup-x64.exe). Xem docs/INSTALL.md mục gỡ thủ công.", true);
            return;
        }
        if (!DialogWindow.ReAuthenticate(Owner, "gỡ cài đặt Con Ngoan")) return;
        _c.Logs.Warn(LogCategory.Admin, "Phụ huynh bắt đầu gỡ cài đặt Con Ngoan.");
        Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true });
        Application.Current.Shutdown(0);
    }

    private void OnTelegramConfigChanged(object sender, RoutedEventArgs e)
    {
        if (TelegramConfigPanel is not null)
            TelegramConfigPanel.Visibility = TelegramEnabledBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveTelegram(object sender, RoutedEventArgs e)
    {
        var enabled = TelegramEnabledBox.IsChecked == true;
        var token = TelegramTokenBox.Text.Trim();
        var chatId = TelegramChatIdBox.Text.Trim();
        var notify = TelegramNotifyBox.IsChecked == true;
        var dailyReport = TelegramDailyReportBox.IsChecked == true;
        var dailyTime = TelegramDailyTimeBox.Text.Trim();

        if (enabled && (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)))
        {
            _status.Show("Vui lòng nhập đầy đủ Bot Token và Chat ID của Telegram.", true);
            return;
        }
        if (chatId.Length > 0 && (!long.TryParse(chatId, out var id) || id <= 0))
        {
            _status.Show("Chat ID phải là số dương của tài khoản Telegram cá nhân (lấy từ @userinfobot). Không dùng Chat ID của nhóm.", true);
            return;
        }
        if (!TimeSpan.TryParse(string.IsNullOrEmpty(dailyTime) ? "21:00" : dailyTime, out var at) || at < TimeSpan.Zero || at >= TimeSpan.FromDays(1))
        {
            _status.Show("Giờ gửi báo cáo phải có dạng HH:mm, ví dụ 21:00.", true);
            return;
        }

        _c.Settings.SetBool(SettingKeys.TelegramEnabled, enabled);
        _c.Settings.Set(SettingKeys.TelegramBotToken, token);
        _c.Settings.Set(SettingKeys.TelegramChatId, chatId);
        _c.Settings.SetBool(SettingKeys.TelegramNotifyExtraTime, notify);
        _c.Settings.SetBool(SettingKeys.TelegramDailyReport, dailyReport);
        _c.Settings.Set(SettingKeys.TelegramDailyReportTime, string.IsNullOrEmpty(dailyTime) ? "21:00" : dailyTime);

        _c.Logs.Info(LogCategory.Settings, $"Đã lưu cấu hình Telegram: {(enabled ? "Bật" : "Tắt")}.");
        _status.Show("Đã lưu cấu hình điều khiển từ xa qua Telegram thành công.", false);
    }

    private async void OnTestTelegram(object sender, RoutedEventArgs e)
    {
        var token = TelegramTokenBox.Text.Trim();
        var chatId = TelegramChatIdBox.Text.Trim();

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
        {
            _status.Show("Hãy nhập Bot Token và Chat ID trước khi thử nghiệm.", true);
            return;
        }

        TestTelegramButton.IsEnabled = false;
        try
        {
            using var client = new TelegramBotClient(token);
            var (tokenOk, botName, tokenErr) = await client.TestTokenAsync();
            if (!tokenOk)
            {
                _status.Show($"Bot Token không hợp lệ: {tokenErr}", true);
                return;
            }

            var text = $"🎉 <b>Con Ngoan kết nối thành công!</b>\n\n" +
                       $"• Máy tính: <b>{Environment.MachineName}</b>\n" +
                       $"• Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n\n" +
                       $"Từ bây giờ bạn sẽ nhận được thông báo khi con xin thêm giờ và có thể duyệt trực tiếp trên Telegram.";

            var (sendOk, sendErr) = await client.SendMessageAsync(chatId, text);
            if (sendOk)
            {
                _status.Show($"Kết nối thành công với bot {botName}! Đã gửi tin nhắn thử nghiệm tới điện thoại.", false);
            }
            else
            {
                _status.Show($"Bot {botName} hợp lệ nhưng không gửi được tới Chat ID {chatId}: {sendErr}. Hãy chắc chắn bạn đã bấm Start trò chuyện với bot trên Telegram.", true);
            }
        }
        catch (Exception ex)
        {
            _status.Show("Lỗi kết nối Telegram: " + ex.Message, true);
        }
        finally
        {
            TestTelegramButton.IsEnabled = true;
        }
    }
}
