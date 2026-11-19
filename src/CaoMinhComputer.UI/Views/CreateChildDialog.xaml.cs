using System.Windows;
using System.Windows.Input;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.UI.Views;

/// <summary>Tạo tài khoản Windows Standard User cho con ngay trong ứng dụng.</summary>
public partial class CreateChildDialog : Window
{
    public string? CreatedSid { get; private set; }
    public string? CreatedName { get; private set; }
    public ApplyResult? Result { get; private set; }

    private CreateChildDialog(Window? owner)
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
        Loaded += (_, _) => NameInput.Focus();
    }

    /// <summary>Hiện hộp thoại; trả về null nếu phụ huynh hủy.</summary>
    public static CreateChildDialog? Run(Window? owner)
    {
        var d = new CreateChildDialog(owner);
        return d.ShowDialog() == true ? d : null;
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        var password = PasswordInput.Password;
        var error = LocalAccountService.ValidateNewUserName(name)
                    ?? LocalAccountService.ValidateNewPassword(password, ConfirmInput.Password, name);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        OkButton.IsEnabled = false;
        OkButton.Content = "Đang tạo...";
        var fullName = FullNameInput.Text;
        string? sid = null;
        var result = await Task.Run(() => LocalAccountService.CreateStandardUser(name, password, fullName, out sid));
        PasswordInput.Clear();
        ConfirmInput.Clear();
        if (!result.Success)
        {
            OkButton.IsEnabled = true;
            OkButton.Content = "Tạo tài khoản";
            ShowError(result.Message);
            return;
        }

        Result = result;
        CreatedSid = sid;
        CreatedName = name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
