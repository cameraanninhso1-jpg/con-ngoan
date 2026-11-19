using System.IO.Pipes;
using System.Text.Json;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Giao thức giữa Con Ngoan Agent (chạy trong phiên của con, quyền người dùng thường) và dịch vụ.
/// Pipe cho phép Authenticated Users kết nối, nhưng dịch vụ chỉ phục vụ khi tiến trình kết nối đúng là
/// ConNgoan.Agent.exe trong thư mục cài đặt (con không ghi được vào Program Files) và chỉ trả dữ liệu của chính phiên đó.
/// </summary>
public static class AgentProtocol
{
    public const string PipeName = "ConNgoan.Agent.v1";
    public const string CmdReport = "report";
    public const string CmdRequest = "request";
    public const string AgentExeName = "ConNgoan.Agent.exe";
    public const string ServiceExeName = "ConNgoan.Service.exe";

    public sealed record Request(
        string Cmd,
        int IdleSeconds = 0,
        int Minutes = 0,
        string? Reason = null);

    public sealed record Response(
        bool Ok,
        string? Error,
        bool IsChild,
        bool Managed,
        bool Allowed,
        int? RemainingSeconds,
        string? Description,
        string? RequestStatus,
        int RequestMinutes,
        long RequestId,
        bool InBreak);

    /// <summary>Phía Agent: gửi một yêu cầu, xác minh đầu bên kia là dịch vụ Con Ngoan.</summary>
    public static async Task<Response?> SendAsync(Request request, string expectedServiceExe, int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutMs, cts.Token).ConfigureAwait(false);
            if (!WindowsSessions.IsPipeServer(client.SafePipeHandle, expectedServiceExe)) return null;
            await PipeProtocol.WriteLineAsync(client, JsonSerializer.Serialize(request, PipeProtocol.Json), cts.Token).ConfigureAwait(false);
            var line = await PipeProtocol.ReadLineAsync(client, cts.Token).ConfigureAwait(false);
            return line is null ? null : JsonSerializer.Deserialize<Response>(line, PipeProtocol.Json);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
