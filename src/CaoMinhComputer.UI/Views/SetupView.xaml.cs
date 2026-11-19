using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.UI.Views;

public partial class SetupView : UserControl
{
    private readonly MainWindow _window;

    public SetupView(MainWindow window)
    {
        InitializeComponent();
        _window = window;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    internal static string? ValidateUsername(string username)
    {
        if (username.Length is < 3 or > 32) return "Tên đăng nhập cần từ 3 đến 32 ký tự.";
        if (!Regex.IsMatch(username, @"^[\p{L}\p{N}._-]+$")) return "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang.";
        return null;
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var error = ValidateUsername(username) ?? PasswordHasher.ValidateStrength(PasswordBox.Password);
        if (error is null && PasswordBox.Password != ConfirmBox.Password) error = "Hai mật khẩu không khớp.";
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        CreateButton.IsEnabled = false;
        var password = PasswordBox.Password;
        var hash = await Task.Run(() => PasswordHasher.Hash(password));
        if (AppServices.Coordinator.Admin.Exists())
        {
            ErrorText.Text = "Tài khoản quản trị đã tồn tại.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        AppServices.Coordinator.Admin.Create(username, hash);
        AppServices.Coordinator.Admin.TouchLogin();
        AppServices.Coordinator.Logs.Info(LogCategory.Admin, $"Đã tạo tài khoản quản trị \"{username}\".");
        AppServices.CurrentAdmin = username;
        _window.ShowShell();
    }
}
