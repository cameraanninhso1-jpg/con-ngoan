using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaoMinhComputer.UI.Views.Pages;

namespace CaoMinhComputer.UI.Views;

public partial class ShellView : UserControl
{
    private readonly MainWindow _window;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public ShellView(MainWindow window)
    {
        InitializeComponent();
        _window = window;
        var name = AppServices.CurrentAdmin ?? "";
        AdminName.Text = name;
        AvatarText.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        HotlineText.Text = $"Hotline {AppServices.Hotline}";
        WebsiteLink.Content = AppServices.WebsiteDisplay;
        VersionText.Text = $"Phiên bản {AppServices.Version}\nCÔNG TY TNHH CAO MINH THĂNG LONG";

        _statusTimer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) =>
        {
            NavWeb.IsChecked = true;
            RefreshStatus();
            _statusTimer.Start();
        };
        Unloaded += (_, _) => _statusTimer.Stop();
    }

    public void RefreshStatus()
    {
        RefreshUpdateLink();
        var paused = AppServices.Coordinator.IsPaused;
        var state = AppServices.GetServiceState();
        if (paused)
        {
            SetStatus("#FBBA1F", "Bảo vệ đang tạm tắt", "Bấm để bật lại trong Bảo mật & gỡ app");
        }
        else
        {
            switch (state)
            {
                case AppServices.ServiceState.Running:
                    SetStatus("#2FD27A", "Đang bảo vệ", "Dịch vụ nền đang chạy, tự phục hồi cấu hình");
                    break;
                case AppServices.ServiceState.NotInstalled:
                    SetStatus("#FF6B6B", "Dịch vụ nền chưa cài", "Chặn vẫn có hiệu lực nhưng không tự phục hồi");
                    break;
                case AppServices.ServiceState.Stopped:
                    SetStatus("#FF6B6B", "Dịch vụ nền đã dừng", "Chặn vẫn có hiệu lực nhưng không tự phục hồi");
                    break;
                default:
                    SetStatus("#FBBA1F", "Dịch vụ đang khởi động", "Vui lòng chờ...");
                    break;
            }
        }
    }

    private void SetStatus(string color, string title, string detail)
    {
        StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        PageHost.Content = sender switch
        {
            _ when sender == NavWeb => new WebBlockPage(),
            _ when sender == NavInstall => new InstallLockPage(),
            _ when sender == NavBreak => new BreakPage(),
            _ when sender == NavTime => new TimePage(),
            _ when sender == NavAdmin => new AdminAccountPage(),
            _ when sender == NavSecurity => new SecurityPage(this),
            _ when sender == NavLogs => new LogPage(),
            _ => new AboutPage()
        };
    }

    private void RefreshUpdateLink()
    {
        string? version = null;
        try
        {
            if (AppServices.Coordinator.Settings.Get(Data.Repositories.SettingKeys.UpdateAvailable) is { } json &&
                System.Text.Json.JsonSerializer.Deserialize<Core.Services.UpdateManifest>(json) is { } m &&
                System.Version.TryParse(m.Version, out _) &&
                Core.Services.UpdateVerifier.IsNewer(m, typeof(ShellView).Assembly.GetName().Version ?? new Version(1, 0)))
                version = m.Version;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        UpdateLink.Content = version is null ? null : $"● Có bản mới {version} – cập nhật";
        UpdateLink.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUpdateLink(object sender, RoutedEventArgs e) => NavAbout.IsChecked = true;

    private void OnStatusClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => NavSecurity.IsChecked = true;

    private void OnLogout(object sender, RoutedEventArgs e) => _window.Logout();

    private void OnSwitchUser(object sender, RoutedEventArgs e) => AppServices.SwitchWindowsUser(Window.GetWindow(this));

    private void OnWebsite(object sender, RoutedEventArgs e) => AppServices.OpenWebsite();
}
