using System.Windows;
using System.Windows.Threading;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Services;
using CaoMinhComputer.UI.Views;

namespace CaoMinhComputer.UI;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();

        // Chế độ dòng lệnh do bộ cài gọi (không hiển thị giao diện).
        if (args.Contains("--prepare-data"))
        {
            Shutdown(RunHeadless(() =>
            {
                var coordinator = Bootstrap();
                coordinator.Logs.Info(LogCategory.System, "Bộ cài đã chuẩn bị thư mục dữ liệu và database.");
                if (coordinator.Settings.GetBool(Data.Repositories.SettingKeys.Uninstalled))
                {
                    // Cài lại sau khi gỡ nhưng giữ dữ liệu: bật lại bảo vệ theo cấu hình cũ (dịch vụ nền sẽ áp dụng).
                    coordinator.Settings.Delete(Data.Repositories.SettingKeys.Uninstalled);
                    coordinator.Settings.SetBool(Data.Repositories.SettingKeys.ProtectionPaused, false);
                    coordinator.Logs.Info(LogCategory.System, "Cài lại với dữ liệu cũ: bật lại bảo vệ theo cấu hình đã lưu.");
                }
            }));
            return;
        }
        if (args.Contains("--uninstall-cleanup"))
        {
            Shutdown(RunHeadless(() =>
            {
                if (!File.Exists(AppPaths.DatabasePath))
                {
                    // Không có database: vẫn gỡ khối HOSTS do ta tạo (nếu có).
                    new HostsBlockingProvider().RemoveAll();
                    return;
                }
                Bootstrap().UninstallCleanup();
            }));
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        _singleInstance = new Mutex(true, @"Global\ConNgoan.UI", out var isFirst);
        if (!isFirst)
        {
            DialogWindow.ShowMessage(null, "Con Ngoan đang mở", "Ứng dụng đã được mở ở một cửa sổ khác.");
            Shutdown(0);
            return;
        }

        if (!AppPaths.IsElevated())
        {
            DialogWindow.ShowMessage(null, "Cần quyền quản trị",
                "Con Ngoan cần chạy với quyền Administrator để áp dụng chính sách. Hãy mở lại và chọn \"Yes\" ở hộp thoại UAC.");
            Shutdown(1);
            return;
        }

        try
        {
            AppServices.Coordinator = Bootstrap();
        }
        catch (Exception ex)
        {
            DialogWindow.ShowMessage(null, "Không khởi tạo được dữ liệu",
                "Không mở được database tại " + AppPaths.DatabasePath + ".\n\n" + ex.Message +
                "\n\nXem docs/TROUBLESHOOTING.md để khôi phục.");
            Shutdown(2);
            return;
        }

        var window = new Views.MainWindow();
        MainWindow = window;
        window.Closed += (_, _) => Shutdown(0);
        window.Show();
    }

    private static ProtectionCoordinator Bootstrap()
    {
        AppPaths.EnsureDataDirectory();
        var db = new AppDatabase();
        db.Migrate();
        return new ProtectionCoordinator(db);
    }

    private static int RunHeadless(Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "setup-error.log"),
                    $"{DateTime.Now:O} {ex}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Không thể ghi log – bỏ qua để bộ cài tiếp tục.
            }
            return 3;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            AppServices.Coordinator?.Logs.Error(LogCategory.System, "Lỗi giao diện: " + e.Exception.Message);
        }
        catch (Exception)
        {
            // Bỏ qua lỗi khi ghi log.
        }
        DialogWindow.ShowMessage(MainWindow, "Đã xảy ra lỗi", e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
