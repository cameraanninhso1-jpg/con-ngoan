using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Data.Repositories;

namespace CaoMinhComputer.UI.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _lastInput = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        ShowLoginOrSetup();

        // Tự khóa phiên quản trị khi không thao tác (mặc định 10 phút).
        InputManager.Current.PreProcessInput += (_, _) => _lastInput = DateTime.UtcNow;
        _idleTimer.Tick += (_, _) =>
        {
            if (AppServices.CurrentAdmin is null) return;
            var minutes = AppServices.Coordinator.Settings.GetInt(SettingKeys.IdleLockMinutes, 10);
            if (minutes > 0 && DateTime.UtcNow - _lastInput > TimeSpan.FromMinutes(minutes))
            {
                AppServices.Coordinator.Logs.Info(LogCategory.Admin, $"Tự đăng xuất sau {minutes} phút không thao tác.");
                Logout();
            }
        };
        _idleTimer.Start();
        Closed += (_, _) => _idleTimer.Stop();
    }

    public void ShowLoginOrSetup()
    {
        AppServices.CurrentAdmin = null;
        Host.Content = AppServices.Coordinator.Admin.Exists() ? new LoginView(this) : new SetupView(this);
    }

    public void ShowShell()
    {
        _lastInput = DateTime.UtcNow;
        Host.Content = new ShellView(this);
    }

    public void Logout()
    {
        if (AppServices.CurrentAdmin is not null)
            AppServices.Coordinator.Logs.Info(LogCategory.Admin, $"Quản trị \"{AppServices.CurrentAdmin}\" đăng xuất.");
        ShowLoginOrSetup();
    }
}
