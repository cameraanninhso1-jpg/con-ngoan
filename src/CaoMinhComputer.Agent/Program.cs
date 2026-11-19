using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Agent;

/// <summary>
/// Con Ngoan Agent – chạy trong phiên Windows của con (khởi động cùng Windows qua khóa Run, quyền người dùng thường).
/// - Báo thời gian không thao tác cho dịch vụ (để không tính giờ khi con rời máy; video đang phát vẫn tính).
/// - Biểu tượng khay hệ thống: thời gian còn lại, nút "Xin thêm thời gian".
/// - Tự thoát nếu tài khoản hiện tại không phải tài khoản của con.
/// Dòng lệnh (dùng cho kiểm thử): --report-once | --request &lt;phút&gt; [lý do]
/// Mã thoát: 0 = thành công, 2 = bị từ chối/không phải tài khoản con, 3 = không kết nối được dịch vụ.
/// </summary>
internal static class Program
{
    internal static string ServicePath => Path.Combine(AppContext.BaseDirectory, AgentProtocol.ServiceExeName);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] is "--report-once" or "--request")
            return RunCommandLine(args);

        using var mutex = new Mutex(true, @"Local\ConNgoan.Agent", out var isFirst);
        if (!isFirst) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new AgentContext());
        return 0;
    }

    private static int RunCommandLine(string[] args)
    {
        AgentProtocol.Request request = args[0] == "--report-once"
            ? new(AgentProtocol.CmdReport, IdleSeconds: IdleDetector.GetEffectiveIdleSeconds())
            : new(AgentProtocol.CmdRequest, Minutes: args.Length > 1 && int.TryParse(args[1], out var m) ? m : 0,
                Reason: args.Length > 2 ? string.Join(' ', args[2..]) : null);
        var response = AgentProtocol.SendAsync(request, ServicePath).GetAwaiter().GetResult();
        if (response is null) return 3;
        return response.Ok && response.IsChild ? 0 : 2;
    }
}
