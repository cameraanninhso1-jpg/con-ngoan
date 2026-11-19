using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaoMinhComputer.Service.Ipc;

/// <summary>
/// Named Pipe cục bộ cho giao diện quản trị (không mở cổng mạng).
/// - ACL: chỉ SYSTEM và Administrators (token đã nâng quyền; token admin chưa nâng quyền có SID Administrators "deny-only" nên bị từ chối).
/// - FirstPipeInstance: không cho tiến trình khác chiếm tên pipe trước.
/// - Chỉ chấp nhận lệnh trong danh sách cho phép, thông điệp tối đa 4 KB, timeout 3 giây.
/// </summary>
public sealed class PipeServerWorker(ILogger<PipeServerWorker> logger) : BackgroundService
{
    private ProtectionCoordinator? _c;

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
            logger.LogError(ex, "Pipe server không khởi tạo được database.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateServer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Không tạo được named pipe (có thể tên pipe đã bị tiến trình khác chiếm).");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
                continue;
            }

            await using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(stoppingToken);
                    await HandleAsync(server, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
                {
                    logger.LogDebug(ex, "Kết nối pipe lỗi.");
                }
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, PipeProtocol.MaxMessageBytes, PipeProtocol.MaxMessageBytes, security);
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken stoppingToken)
    {
        string? line;
        using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            line = await PipeProtocol.ReadLineAsync(server, readTimeout.Token);
        }
        PipeProtocol.Response response;
        var request = line is null ? null : JsonSerializer.Deserialize<PipeProtocol.Request>(line, PipeProtocol.Json);
        if (request?.Cmd is not { } cmd || !PipeProtocol.AllowedCommands.Contains(cmd))
        {
            response = new PipeProtocol.Response(false, "Lệnh không hợp lệ.", Version, false, null);
        }
        else if (cmd == PipeProtocol.CmdRefresh)
        {
            ServiceSignals.Wake();
            ServiceSignals.RaiseSessionChanged(); // kiểm tra lại chặn website/ứng dụng ngay theo cấu hình mới
            response = new PipeProtocol.Response(true, null, Version, InBreak(), null);
        }
        else if (cmd == PipeProtocol.CmdAppLocker)
        {
            // Dịch vụ chạy SYSTEM nên ghi được AppLocker CSP; cấu hình lấy từ database (không nhận dữ liệu chặn qua pipe).
            var result = _c is null ? ApplyResult.Fail("Dịch vụ chưa sẵn sàng.") : await Task.Run(() => _c.ApplyAppLocker(), stoppingToken);
            response = new PipeProtocol.Response(result.Success, result.Success ? null : result.Message, Version, InBreak(), null, result.Message);
        }
        else if (cmd == PipeProtocol.CmdUpdateCheck)
        {
            var runner = Workers.UpdateRunner.Instance;
            if (runner is null)
            {
                response = new PipeProtocol.Response(false, "Dịch vụ chưa sẵn sàng.", Version, InBreak(), null);
            }
            else
            {
                using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                checkTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                var r = await runner.CheckAsync(manual: true, checkTimeout.Token);
                response = new PipeProtocol.Response(r.Ok, r.Ok ? null : r.Message, Version, InBreak(), null, r.Message);
            }
        }
        else if (cmd == PipeProtocol.CmdUpdateInstall)
        {
            var runner = Workers.UpdateRunner.Instance;
            var message = runner?.StartInstall("Phụ huynh") ?? "Dịch vụ chưa sẵn sàng.";
            response = new PipeProtocol.Response(runner is not null, runner is null ? message : null, Version, InBreak(), null, message);
        }
        else if (cmd == PipeProtocol.CmdStatus)
        {
            var children = _c?.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var sessions = WindowsSessions.Enumerate()
                .Select(s => new PipeProtocol.SessionInfo(s.SessionId, s.UserName, s.Sid,
                    s.Sid is not null && children.Contains(s.Sid), s.IsActive, s.IsLocked))
                .ToList();
            response = new PipeProtocol.Response(true, null, Version, InBreak(), sessions);
        }
        else
        {
            response = new PipeProtocol.Response(true, null, Version, InBreak(), null);
        }
        // Hạn ghi phản hồi tính từ lúc xử lý xong (áp dụng AppLocker có thể mất vài giây).
        using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        writeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        await PipeProtocol.WriteLineAsync(server, JsonSerializer.Serialize(response, PipeProtocol.Json), writeTimeout.Token);
    }

    private bool InBreak()
    {
        try { return _c?.IsInBreakNow() ?? false; }
        catch (Exception) { return false; }
    }

    private static string Version =>
        typeof(PipeServerWorker).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
