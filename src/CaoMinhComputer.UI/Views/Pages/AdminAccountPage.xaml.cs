using System.Windows;
using System.Windows.Controls;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.UI.ViewModels;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class AdminAccountPage : UserControl
{
    private readonly Data.Services.ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private bool _loading = true;

    public AdminAccountPage()
    {
        InitializeComponent();
        Banner.DataContext = _status;
        LoadInfo();
        var minutes = _c.Settings.GetInt(SettingKeys.IdleLockMinutes, 10).ToString();
        IdleCombo.SelectedItem = IdleCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == minutes)
                                 ?? IdleCombo.Items[1];
        _loading = false;
    }

    private void LoadInfo()
    {
        var admin = _c.Admin.Get();
        if (admin is null) return;
        NameText.Text = admin.Username;
        AvatarText.Text = admin.Username[..1].ToUpperInvariant();
        InfoText.Text = $"Tạo ngày {admin.CreatedUtc.ToLocalTime():dd/MM/yyyy}" +
                        (admin.LastLoginUtc is { } last ? $" · Đăng nhập gần nhất {last.ToLocalTime():dd/MM/yyyy HH:mm}" : "");
    }

    private async Task<bool> VerifyCurrentAsync(string password)
    {
        var admin = _c.Admin.Get();
        return admin is not null && await Task.Run(() => PasswordHasher.Verify(password, admin.PasswordHash));
    }

    private async void OnChangeUsername(object sender, RoutedEventArgs e)
    {
        var newName = NewUsernameBox.Text.Trim();
        var error = SetupView.ValidateUsername(newName);
        if (error is not null)
        {
            _status.Show(error, true);
            return;
        }
        if (!await VerifyCurrentAsync(UsernamePasswordBox.Password))
        {
            _c.Logs.Warn(LogCategory.Admin, "Đổi tên đăng nhập thất bại: sai mật khẩu hiện tại.");
            _status.Show("Mật khẩu hiện tại không đúng.", true);
            UsernamePasswordBox.Clear();
            return;
        }
        var admin = _c.Admin.Get()!;
        _c.Admin.Update(newName, admin.PasswordHash);
        _c.Logs.Info(LogCategory.Admin, $"Đổi tên đăng nhập quản trị từ \"{admin.Username}\" thành \"{newName}\".");
        AppServices.CurrentAdmin = newName;
        NewUsernameBox.Clear();
        UsernamePasswordBox.Clear();
        LoadInfo();
        _status.Show("Đã đổi tên đăng nhập. Tên mới có hiệu lực từ lần đăng nhập sau.", false);
    }

    private async void OnChangePassword(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password;
        var error = PasswordHasher.ValidateStrength(newPassword);
        if (error is null && newPassword != ConfirmPasswordBox.Password) error = "Hai mật khẩu mới không khớp.";
        if (error is null && newPassword == CurrentPasswordBox.Password) error = "Mật khẩu mới phải khác mật khẩu hiện tại.";
        if (error is not null)
        {
            _status.Show(error, true);
            return;
        }
        if (!await VerifyCurrentAsync(CurrentPasswordBox.Password))
        {
            _c.Logs.Warn(LogCategory.Admin, "Đổi mật khẩu thất bại: sai mật khẩu hiện tại.");
            _status.Show("Mật khẩu hiện tại không đúng.", true);
            CurrentPasswordBox.Clear();
            return;
        }
        var hash = await Task.Run(() => PasswordHasher.Hash(newPassword));
        var admin = _c.Admin.Get()!;
        _c.Admin.Update(admin.Username, hash);
        _c.Logs.Info(LogCategory.Admin, "Đã đổi mật khẩu quản trị.");
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        _status.Show("Đã đổi mật khẩu quản trị thành công.", false);
    }

    private void OnIdleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IdleCombo.SelectedItem is not ComboBoxItem item) return;
        _c.Settings.Set(SettingKeys.IdleLockMinutes, (string)item.Tag);
        _c.Logs.Info(LogCategory.Settings, $"Tự đăng xuất: {item.Content}.");
        _status.Show($"Đã lưu: {item.Content}.", false);
    }
}
