using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Giao thức Named Pipe giữa giao diện và dịch vụ: một dòng JSON yêu cầu → một dòng JSON trả lời.
/// Pipe chỉ cho SYSTEM + Administrators (token đã nâng quyền) kết nối; tối đa 4 KB mỗi thông điệp.
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "ConNgoan.Service.v1";
    public const int MaxMessageBytes = 4096;

    public const string CmdPing = "ping";
    public const string CmdStatus = "status";
    public const string CmdRefresh = "refresh";
    /// <summary>Áp dụng lại AppLocker theo cấu hình trong database (chỉ dịch vụ SYSTEM ghi được AppLocker CSP).</summary>
    public const string CmdAppLocker = "applocker";

    /// <summary>Dịch vụ tải version.json + chữ ký, xác minh và lưu kết quả (giao diện đọc lại từ database).</summary>
    public const string CmdUpdateCheck = "update-check";
    /// <summary>Dịch vụ tải bộ cài mới, kiểm tra SHA-256 theo manifest đã ký rồi cài im lặng (dịch vụ sẽ khởi động lại).</summary>
    public const string CmdUpdateInstall = "update-install";

    public static readonly HashSet<string> AllowedCommands = [CmdPing, CmdStatus, CmdRefresh, CmdAppLocker, CmdUpdateCheck, CmdUpdateInstall];

    public sealed record Request(string Cmd);

    public sealed record SessionInfo(int SessionId, string UserName, string? Sid, bool IsChild, bool IsActive, bool IsLocked);

    public sealed record Response(bool Ok, string? Error, string Version, bool InBreak, IReadOnlyList<SessionInfo>? Sessions, string? Message = null);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Đọc một dòng giới hạn kích thước (chống thông điệp quá lớn).</summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, total);
            if (newline >= 0) return Encoding.UTF8.GetString(buffer, 0, newline);
        }
        return total >= buffer.Length ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    public static async Task WriteLineAsync(Stream stream, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("Thông điệp quá lớn.");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi lệnh tới dịch vụ (phía giao diện). Kiểm tra tiến trình phía server đúng là ConNgoan.Service.exe
    /// trước khi tin dữ liệu trả về. Trả về null nếu dịch vụ không phản hồi.
    /// </summary>
    public static async Task<Response?> SendAsync(string cmd, string expectedServiceExe, int timeoutMs = 2000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutMs, cts.Token).ConfigureAwait(false);
            if (!WindowsSessions.IsPipeServer(client.SafePipeHandle, expectedServiceExe)) return null;
            await WriteLineAsync(client, JsonSerializer.Serialize(new Request(cmd), Json), cts.Token).ConfigureAwait(false);
            var line = await ReadLineAsync(client, cts.Token).ConfigureAwait(false);
            return line is null ? null : JsonSerializer.Deserialize<Response>(line, Json);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
