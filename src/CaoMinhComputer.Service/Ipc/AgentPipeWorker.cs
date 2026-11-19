using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaoMinhComputer.Service.Ipc;

/// <summary>Báo cáo idle mới nhất của Agent theo phiên (TimeWorker dùng để không tính thời gian không thao tác).</summary>
public static class AgentRegistry
{
    public sealed record Report(int IdleSeconds, DateTime ReceivedUtc);

    private static readonly ConcurrentDictionary<int, Report> Reports = new();

    public static void Set(int sessionId, int idleSeconds) => Reports[sessionId] = new Report(idleSeconds, DateTime.UtcNow);

    /// <summary>Phiên đang "không thao tác" nếu Agent báo idle ≥ ngưỡng trong vòng 90 giây gần nhất.
    /// Không có báo cáo (Agent bị tắt) → coi như đang dùng (an toàn cho phụ huynh).</summary>
    public static bool IsIdle(int sessionId, int thresholdSeconds) =>
        Reports.TryGetValue(sessionId, out var r) && DateTime.UtcNow - r.ReceivedUtc < TimeSpan.FromSeconds(90) && r.IdleSeconds >= thresholdSeconds;
}

/// <summary>
/// Named pipe cho Agent chạy trong phiên của con.
/// - ACL: Authenticated Users đọc/ghi (Agent chạy quyền người dùng), SYSTEM/Administrators toàn quyền.
/// - Xác thực: tiến trình client phải đúng là ConNgoan.Agent.exe trong thư mục cài đặt; SID và phiên lấy từ chính tiến trình
///   (không tin dữ liệu do client tự khai). Luôn có sẵn một instance chờ để không có khoảng trống cho tiến trình khác chiếm tên pipe.
/// </summary>
public sealed class AgentPipeWorker(ILogger<AgentPipeWorker> logger) : BackgroundService
{
    private const int MaxInstances = 8;
    private ProtectionCoordinator? _c;
    private readonly string _agentPath = Path.Combine(AppContext.BaseDirectory, AgentProtocol.AgentExeName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            var db = new AppDatabase();
            db.Migrate();
            _c = new ProtectionCoordinator(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent pipe không khởi tạo được database.");
            return;
        }

        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateServer(first);
                first = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Không tạo được named pipe cho Agent.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
                first = true;
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }

            // Xử lý song song; vòng lặp tạo ngay instance kế tiếp nên tên pipe luôn thuộc về dịch vụ.
            _ = Task.Run(async () =>
            {
                await using (server)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(3));
                        await HandleAsync(server, timeout.Token);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
                    {
                        logger.LogDebug(ex, "Kết nối Agent lỗi.");
                    }
                }
            }, CancellationToken.None);
        }
    }

    private static NamedPipeServerStream CreateServer(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(AgentProtocol.PipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
            options, PipeProtocol.MaxMessageBytes, PipeProtocol.MaxMessageBytes, security);
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var c = _c!;
        var line = await PipeProtocol.ReadLineAsync(server, ct);
        var client = WindowsSessions.GetPipeClient(server.SafePipeHandle);

        AgentProtocol.Response response;
        if (client is null || client.OwnerSid is null || client.ImagePath is null ||
            !string.Equals(Path.GetFullPath(client.ImagePath), Path.GetFullPath(_agentPath), StringComparison.OrdinalIgnoreCase))
        {
            response = Error("Tiến trình không được phép kết nối.");
        }
        else
        {
            var request = line is null ? null : JsonSerializer.Deserialize<AgentProtocol.Request>(line, PipeProtocol.Json);
            response = request?.Cmd switch
            {
                AgentProtocol.CmdReport => Report(c, client, request),
                AgentProtocol.CmdRequest => SubmitRequest(c, client, request),
                _ => Error("Lệnh không hợp lệ.")
            };
        }
        await PipeProtocol.WriteLineAsync(server, JsonSerializer.Serialize(response, PipeProtocol.Json), ct);
    }

    private static AgentProtocol.Response Report(ProtectionCoordinator c, WindowsSessions.PipeClient client, AgentProtocol.Request request)
    {
        var isChild = c.Settings.GetChildSids().Contains(client.OwnerSid!, StringComparer.OrdinalIgnoreCase);
        if (isChild) AgentRegistry.Set(client.SessionId, Math.Clamp(request.IdleSeconds, 0, 86_400));
        return Status(c, client.OwnerSid!, isChild, ok: true, error: null);
    }

    private static AgentProtocol.Response SubmitRequest(ProtectionCoordinator c, WindowsSessions.PipeClient client, AgentProtocol.Request request)
    {
        var result = c.SubmitExtraRequest(client.OwnerSid!, request.Minutes, request.Reason);
        var isChild = c.Settings.GetChildSids().Contains(client.OwnerSid!, StringComparer.OrdinalIgnoreCase);
        if (result.Success)
        {
            ServiceSignals.Wake();
            var latest = c.Requests.Latest(client.OwnerSid!);
            if (latest is not null)
                ServiceSignals.RaiseExtraRequestSubmitted(latest.Id, client.OwnerSid!, request.Minutes, request.Reason);
        }
        return Status(c, client.OwnerSid!, isChild, result.Success, result.Success ? result.Message : result.Message);
    }


    private static AgentProtocol.Response Status(ProtectionCoordinator c, string sid, bool isChild, bool ok, string? error)
    {
        if (!isChild)
            return new AgentProtocol.Response(ok, error, false, false, true, null, null, null, 0, 0, false);
        var status = c.GetTimeStatus(sid, DateTime.UtcNow);
        var latest = c.Requests.Latest(sid);
        var managed = c.Settings.GetTimeConfig().Enabled;
        return new AgentProtocol.Response(ok, error, true, managed, status.Allowed, status.RemainingSeconds, status.Description,
            latest?.Status, latest?.Minutes ?? 0, latest?.Id ?? 0, c.IsInBreakNow());
    }

    private static AgentProtocol.Response Error(string message) =>
        new(false, message, false, false, true, null, null, null, 0, 0, false);
}
