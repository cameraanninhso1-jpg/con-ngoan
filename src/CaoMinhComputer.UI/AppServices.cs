using System.ServiceProcess;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Services;

namespace CaoMinhComputer.UI;

/// <summary>Điểm truy cập dịch vụ dùng chung cho giao diện (composition root đơn giản).</summary>
public static class AppServices
{
    public static ProtectionCoordinator Coordinator { get; set; } = null!;

    /// <summary>Tên quản trị của phiên đăng nhập hiện tại (null = chưa đăng nhập).</summary>
    public static string? CurrentAdmin { get; set; }

    public const string Website = "https://caominhcomputer.com/";
    public const string WebsiteDisplay = "caominhcomputer.com";
    public const string Hotline = "0936.203.209";

    /// <summary>
    /// Mở website bằng trình duyệt của người dùng. Gọi qua explorer.exe để trình duyệt KHÔNG chạy với quyền
    /// Administrator kế thừa từ giao diện quản trị.
    /// </summary>
    public static void OpenWebsite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Website) { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Không mở được trình duyệt – bỏ qua.
        }
    }

    /// <summary>
    /// "Đổi người dùng Windows": đăng xuất khỏi giao diện quản trị rồi đưa Windows về màn hình đăng nhập để chọn
    /// tài khoản khác (ví dụ tài khoản của con). Ứng dụng đang mở của phụ huynh vẫn giữ nguyên.
    /// </summary>
    public static void SwitchWindowsUser(System.Windows.Window? owner, string? hint = null)
    {
        var text = (hint is null ? "" : hint + "\n\n") +
                   "Màn hình sẽ chuyển về trang đăng nhập Windows. Chọn tài khoản cần dùng ở góc dưới bên trái rồi đăng nhập.\n\n" +
                   "Các ứng dụng bạn đang mở vẫn được giữ nguyên; giao diện quản trị Con Ngoan sẽ đăng xuất để con không dùng được.";
        if (WindowsSessions.IsFastUserSwitchingHidden())
            text += "\n\n⚠ Máy đang bật chính sách ẩn \"Chuyển người dùng\" (HideFastUserSwitching) – có thể phải đăng xuất (Sign out) mới chọn được tài khoản khác.";
        if (!Views.DialogWindow.Confirm(owner, "Đổi người dùng Windows", text, "Đổi người dùng")) return;

        Coordinator.Logs.Info(Core.Models.LogCategory.Admin, $"Phụ huynh ({CurrentAdmin}) chuyển sang màn hình đăng nhập Windows để đổi người dùng.");
        (System.Windows.Application.Current.MainWindow as Views.MainWindow)?.Logout();
        if (!WindowsSessions.SwitchUser())
            Views.DialogWindow.ShowMessage(owner, "Không đổi được người dùng",
                "Windows không cho phép chuyển người dùng lúc này. Hãy bấm Start › biểu tượng tài khoản › chọn tài khoản khác, hoặc nhấn Windows + L.");
    }

    public static string ServiceExePath => Path.Combine(AppContext.BaseDirectory, "ConNgoan.Service.exe");

    /// <summary>Gửi lệnh tới dịch vụ qua Named Pipe (đã kiểm tra đúng tiến trình dịch vụ). null = dịch vụ không phản hồi.</summary>
    public static Task<PipeProtocol.Response?> SendToServiceAsync(string cmd) => PipeProtocol.SendAsync(cmd, ServiceExePath);

    public static string Version =>
        typeof(AppServices).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    public enum ServiceState
    {
        NotInstalled,
        Running,
        Stopped,
        Pending
    }

    public static ServiceState GetServiceState()
    {
        try
        {
            using var sc = new ServiceController(AppPaths.ServiceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => ServiceState.Running,
                ServiceControllerStatus.Stopped => ServiceState.Stopped,
                _ => ServiceState.Pending
            };
        }
        catch (InvalidOperationException)
        {
            return ServiceState.NotInstalled;
        }
    }
}
