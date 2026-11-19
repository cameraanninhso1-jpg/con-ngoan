using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"Phiên bản {AppServices.Version}";
        HotlineText.Text = $"Hotline: {AppServices.Hotline}";
        WebsiteText.Text = AppServices.WebsiteDisplay;
        EnvText.Text = $"{WindowsEdition.Detect().FriendlyName} · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.ProcessArchitecture}";
        DataText.Text =
            $"Toàn bộ dữ liệu lưu cục bộ tại {AppPaths.DataDir} (SQLite). Chỉ khi phụ huynh tự bật Telegram (mục Bảo mật), " +
            "tên tài khoản của con, tên máy, số phút sử dụng và yêu cầu xin giờ mới được gửi tới Telegram của phụ huynh. " +
            "Thư mục chỉ SYSTEM và Administrators truy cập được. Nhật ký tự xoay vòng (tối đa 50.000 dòng, 180 ngày). " +
            "Con Ngoan không ghi nội dung trang web, phím bấm, ảnh màn hình hay dữ liệu cá nhân. " +
            "Chỉ khi kiểm tra cập nhật, dịch vụ tải file thông tin phiên bản từ caominhcomputer.com (không gửi dữ liệu của bạn).";

        _loading = true;
        AutoCheckToggle.IsChecked = AppServices.Coordinator.Settings.Get(SettingKeys.UpdateAutoCheck) != "0";
        _loading = false;
        _poll.Tick += (_, _) => OnPoll();
        Unloaded += (_, _) => _poll.Stop();
        ShowUpdateState();
    }

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading;
    private bool _installing;

    private SettingsRepository Settings => AppServices.Coordinator.Settings;

    /// <summary>Đọc trạng thái cập nhật mà dịch vụ lưu trong database.</summary>
    private void ShowUpdateState()
    {
        UpdateManifest? available = null;
        if (Settings.Get(SettingKeys.UpdateAvailable) is { } json)
        {
            try { available = JsonSerializer.Deserialize<UpdateManifest>(json); }
            catch (JsonException) { }
        }
        var current = UpdateVerifier.Normalize(typeof(AboutPage).Assembly.GetName().Version ?? new Version(1, 0));
        if (available is not null && System.Version.TryParse(available.Version, out _) && UpdateVerifier.IsNewer(available, current))
        {
            UpdateBanner.Visibility = Visibility.Visible;
            InstallButton.Visibility = Visibility.Visible;
            UpdateTitle.Text = $"Có phiên bản mới {available.Version}" + (available.Published is { } d ? $" (phát hành {d})" : "") +
                               $" · {available.Size / 1024.0 / 1024.0:N1} MB";
            UpdateNotes.Text = string.IsNullOrWhiteSpace(available.Notes) ? "Sửa lỗi và cải thiện độ ổn định." : available.Notes;
            InstallButtonText.Text = $"Cài phiên bản {available.Version}";
        }
        else
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
        }

        var status = Settings.Get(SettingKeys.UpdateStatus);
        var last = Settings.Get(SettingKeys.UpdateLastCheck);
        var lastText = last is not null && DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? $"Kiểm tra lần cuối: {t.ToLocalTime():HH:mm dd/MM/yyyy}."
            : "Chưa kiểm tra lần nào.";
        UpdateStatusText.Text = $"Phiên bản đang dùng: {UpdateVerifier.FormatVersion(current)}. {lastText}" + (status is null ? "" : "\n" + status);
    }

    private void OnAutoCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = AutoCheckToggle.IsChecked == true;
        Settings.Set(SettingKeys.UpdateAutoCheck, on ? "1" : "0");
        AppServices.Coordinator.Logs.Info(LogCategory.Settings, on ? "Bật tự kiểm tra cập nhật mỗi ngày." : "Tắt tự kiểm tra cập nhật.");
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateStatusText.Text = "Đang kiểm tra bản cập nhật...";
        try
        {
            var r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateCheck, AppServices.ServiceExePath, timeoutMs: 60_000);
            if (r is null)
                Settings.Set(SettingKeys.UpdateStatus, "Không kết nối được dịch vụ Con Ngoan – dịch vụ cần đang chạy để kiểm tra cập nhật.");
        }
        finally
        {
            CheckButton.IsEnabled = true;
            ShowUpdateState();
        }
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (!DialogWindow.Confirm(window, "Cài bản cập nhật",
                "Dịch vụ sẽ tải bộ cài, kiểm tra chữ ký số của Cao Minh rồi cài đặt tự động (khoảng 1–2 phút).\n\n" +
                "Trong lúc cài, cửa sổ Con Ngoan sẽ tự đóng; cấu hình chặn vẫn giữ nguyên. Mở lại app sau khi cài xong.",
                "Cài ngay")) return;

        AppServices.Coordinator.Logs.Info(LogCategory.Admin, $"Phụ huynh ({AppServices.CurrentAdmin}) yêu cầu cài bản cập nhật.");
        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        var r = await PipeProtocol.SendAsync(PipeProtocol.CmdUpdateInstall, AppServices.ServiceExePath, timeoutMs: 10_000);
        if (r is null)
        {
            DialogWindow.ShowMessage(window, "Không cập nhật được", "Không kết nối được dịch vụ Con Ngoan. Hãy thử lại sau.");
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
            return;
        }
        _installing = true;
        _poll.Start();
    }

    private void OnPoll()
    {
        ShowUpdateState();
        var status = Settings.Get(SettingKeys.UpdateStatus) ?? "";
        if (status.StartsWith("Đang cài", StringComparison.Ordinal))
        {
            // Bộ cài sắp thay file của giao diện → đóng app để không bị khóa file.
            _poll.Stop();
            DialogWindow.ShowMessage(Window.GetWindow(this), "Đang cập nhật",
                "Bộ cài đã được xác minh và đang cài đặt. Con Ngoan sẽ đóng ngay bây giờ – mở lại sau 1–2 phút.");
            Application.Current.Shutdown();
        }
        else if (_installing && !status.StartsWith("Đang", StringComparison.Ordinal))
        {
            _installing = false;
            _poll.Stop();
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
        }
    }

    private void OnWebsite(object sender, System.Windows.RoutedEventArgs e) => AppServices.OpenWebsite();
}
