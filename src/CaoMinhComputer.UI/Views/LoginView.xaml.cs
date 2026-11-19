using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;

namespace CaoMinhComputer.UI.Views;

public partial class LoginView : UserControl
{
    private const int MaxAttempts = 5;
    private readonly MainWindow _window;

    public LoginView(MainWindow window)
    {
        InitializeComponent();
        _window = window;
        FooterText.Text = $"© CÔNG TY TNHH CAO MINH THĂNG LONG · {AppServices.WebsiteDisplay} · Hotline {AppServices.Hotline} · Phiên bản {AppServices.Version}";
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnLogin(sender, e);
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        var settings = AppServices.Coordinator.Settings;
        var logs = AppServices.Coordinator.Logs;

        var lockout = settings.GetUtc(SettingKeys.LockoutUntil);
        if (lockout is { } until && until > DateTime.UtcNow)
        {
            ShowError($"Nhập sai quá nhiều lần. Thử lại sau {Math.Ceiling((until - DateTime.UtcNow).TotalSeconds)} giây.");
            return;
        }

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            ShowError("Hãy nhập tên đăng nhập và mật khẩu.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Content = "ĐANG KIỂM TRA...";
        var admin = AppServices.Coordinator.Admin.Get();
        // PBKDF2 600.000 vòng mất vài trăm ms – chạy nền để giao diện không đứng.
        var ok = admin is not null && await Task.Run(() =>
            PasswordHasher.Verify(password, admin.PasswordHash) &&
            string.Equals(username, admin.Username, StringComparison.OrdinalIgnoreCase));
        LoginButton.IsEnabled = true;
        LoginButton.Content = "ĐĂNG NHẬP";

        if (!ok)
        {
            var failed = settings.GetInt(SettingKeys.FailedLogins) + 1;
            settings.Set(SettingKeys.FailedLogins, failed.ToString());
            logs.Warn(LogCategory.Admin, $"Đăng nhập thất bại (lần {failed}).");
            if (failed >= MaxAttempts)
            {
                var seconds = 60 * (int)Math.Pow(2, Math.Min(failed - MaxAttempts, 4));
                settings.SetUtc(SettingKeys.LockoutUntil, DateTime.UtcNow.AddSeconds(seconds));
                ShowError($"Sai {failed} lần. Tạm khóa đăng nhập {seconds} giây.");
            }
            else
            {
                ShowError($"Tên đăng nhập hoặc mật khẩu không đúng. Còn {MaxAttempts - failed} lần thử.");
            }
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        settings.Set(SettingKeys.FailedLogins, "0");
        settings.Delete(SettingKeys.LockoutUntil);
        AppServices.Coordinator.Admin.TouchLogin();
        AppServices.CurrentAdmin = admin!.Username;
        logs.Info(LogCategory.Admin, $"Quản trị \"{admin.Username}\" đăng nhập.");
        _window.ShowShell();
    }

    private void OnForgot(object sender, RoutedEventArgs e)
    {
        var message =
            "Con Ngoan không có mật khẩu mặc định hay \"cửa sau\".\n\n" +
            "Vì bạn đã mở ứng dụng bằng quyền Administrator của Windows (qua UAC), bạn có thể đặt lại tài khoản quản trị của " +
            "Con Ngoan. Cấu hình chặn hiện tại được giữ nguyên; thao tác này được ghi vào nhật ký.\n\n" +
            "Bạn có muốn đặt lại và tạo mật khẩu quản trị mới không?";
        if (!DialogWindow.Confirm(_window, "Khôi phục quản trị", message, "Đặt lại")) return;

        AppServices.Coordinator.Admin.Delete();
        AppServices.Coordinator.Settings.Set(SettingKeys.FailedLogins, "0");
        AppServices.Coordinator.Settings.Delete(SettingKeys.LockoutUntil);
        AppServices.Coordinator.Logs.Warn(LogCategory.Admin,
            $"Tài khoản quản trị đã được đặt lại bởi tài khoản Windows \"{Environment.UserName}\" (quyền Administrator).");
        _window.ShowLoginOrSetup();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
