using System.Windows;
using System.Windows.Input;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.UI.Views;

/// <summary>Hộp thoại thống nhất: thông báo, xác nhận và xác thực lại mật khẩu quản trị.</summary>
public partial class DialogWindow : Window
{
    private bool _passwordMode;

    private DialogWindow(Window? owner)
    {
        InitializeComponent();
        if (owner is { IsVisible: true })
            Owner = owner;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    public static void ShowMessage(Window? owner, string title, string message)
    {
        var d = new DialogWindow(owner);
        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.CancelButton.Visibility = Visibility.Collapsed;
        d.OkButton.Content = "Đóng";
        d.ShowDialog();
    }

    public static bool Confirm(Window? owner, string title, string message, string okText = "Đồng ý")
    {
        var d = new DialogWindow(owner);
        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.OkButton.Content = okText;
        return d.ShowDialog() == true;
    }

    /// <summary>Yêu cầu nhập lại mật khẩu quản trị trước thao tác quan trọng. Ghi nhật ký kết quả (không ghi mật khẩu).</summary>
    public static bool ReAuthenticate(Window? owner, string actionDescription)
    {
        var d = new DialogWindow(owner) { _passwordMode = true };
        d.TitleText.Text = "Xác thực quản trị";
        d.MessageText.Text = $"Nhập mật khẩu quản trị để {actionDescription}.";
        d.PasswordPanel.Visibility = Visibility.Visible;
        d.OkButton.Content = "Xác nhận";
        d.Loaded += (_, _) => d.PasswordInput.Focus();
        var ok = d.ShowDialog() == true;
        AppServices.Coordinator.Logs.Add(ok ? LogLevel.Info : LogLevel.Warning, LogCategory.Admin,
            ok ? $"Xác thực lại thành công để {actionDescription}." : $"Hủy/không xác thực được để {actionDescription}.");
        return ok;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_passwordMode)
        {
            var admin = AppServices.Coordinator.Admin.Get();
            if (admin is null || !PasswordHasher.Verify(PasswordInput.Password, admin.PasswordHash))
            {
                PasswordInput.Clear();
                ErrorText.Text = "Mật khẩu không đúng.";
                ErrorText.Visibility = Visibility.Visible;
                PasswordInput.Focus();
                return;
            }
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
