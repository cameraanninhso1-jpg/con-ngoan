using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Database;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaoMinhComputer.Service.Workers;

/// <summary>
/// Thông báo & điều khiển từ xa qua Telegram (chỉ khi phụ huynh bật và nhập Bot Token + Chat ID):
/// - Gửi tin khi con xin thêm giờ (kèm nút duyệt), báo cáo cuối ngày, thông báo hết giờ mượn máy.
/// - Nhận lệnh: /status, /cho, /tuchoi, /khoa, /muonmay (phải bấm xác nhận), /bat.
/// Bảo mật: chỉ chấp nhận tin nhắn RIÊNG (chat private) từ đúng Chat ID của phụ huynh – tin trong nhóm bị bỏ qua.
/// Mọi lệnh được ghi nhật ký. KHÔNG chụp màn hình, không gửi nội dung con xem; chỉ gửi tên tài khoản, tên máy,
/// số phút sử dụng và số lần bị chặn (phụ huynh được thông báo rõ khi bật tính năng).
/// </summary>
public sealed class TelegramWorker(ILogger<TelegramWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ConfirmTtl = TimeSpan.FromMinutes(3);
    private readonly ConcurrentDictionary<string, (int Minutes, DateTime ExpiresUtc)> _pendingBorrow = new();
    private ProtectionCoordinator? _c;

    private sealed record Config(string Token, string ChatId);

    private Config? CurrentConfig()
    {
        if (_c is null || !_c.Settings.GetBool(SettingKeys.TelegramEnabled)) return null;
        var token = _c.Settings.Get(SettingKeys.TelegramBotToken)?.Trim();
        var chatId = _c.Settings.Get(SettingKeys.TelegramChatId)?.Trim();
        return string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId) ? null : new Config(token, chatId);
    }

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
            logger.LogError(ex, "TelegramWorker không khởi tạo được database.");
            return;
        }

        ServiceSignals.ExtraRequestSubmitted += OnChildExtraRequest;
        ServiceSignals.ParentNotice += OnParentNotice;
        long offset = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var config = CurrentConfig();
                if (config is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }
                try
                {
                    using var client = new TelegramBotClient(config.Token);
                    var updates = await client.GetUpdatesAsync(offset, timeoutSeconds: 20, stoppingToken);
                    if (updates is null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // mất mạng / token sai: thử lại chậm
                        continue;
                    }
                    if (updates["result"] is JsonArray list)
                    {
                        foreach (var item in list)
                        {
                            if (item?["update_id"] is JsonNode uid) offset = uid.GetValue<long>() + 1;
                            if (item?["callback_query"] is JsonNode cb) await HandleCallbackAsync(client, cb, config.ChatId, stoppingToken);
                            else if (item?["message"] is JsonNode msg) await HandleMessageAsync(client, msg, config.ChatId, stoppingToken);
                        }
                    }
                    await SendDailyReportIfDueAsync(client, config.ChatId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Lỗi xử lý Telegram.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ServiceSignals.ExtraRequestSubmitted -= OnChildExtraRequest;
            ServiceSignals.ParentNotice -= OnParentNotice;
        }
    }

    private void Send(string text, List<List<(string text, string data)>>? buttons = null)
    {
        if (CurrentConfig() is not { } config) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new TelegramBotClient(config.Token);
                await client.SendMessageAsync(config.ChatId, text, buttons);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Không gửi được tin Telegram.");
            }
        });
    }

    private void OnParentNotice(string text) => Send(WebUtility.HtmlEncode(text));

    private void OnChildExtraRequest(long requestId, string childSid, int minutes, string? reason)
    {
        if (_c is null || !_c.Settings.GetBool(SettingKeys.TelegramNotifyExtraTime, fallback: true)) return;
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "<i>(không ghi lý do)</i>" : WebUtility.HtmlEncode(reason);
        Send($"🔔 <b>Con xin thêm giờ</b>\n👤 Tài khoản: <b>{WebUtility.HtmlEncode(Label(childSid))}</b>\n" +
             $"⏳ Xin: <b>{minutes} phút</b>\n📝 Lý do: {reasonText}\n\n👇 <i>Chọn để duyệt:</i>",
            [
                [($"+{minutes} phút như con xin", $"approve:{requestId}:{minutes}")],
                [("+15", $"approve:{requestId}:15"), ("+30", $"approve:{requestId}:30"), ("+60", $"approve:{requestId}:60")],
                [("❌ Từ chối", $"reject:{requestId}")]
            ]);
    }

    private async Task HandleCallbackAsync(TelegramBotClient client, JsonNode cb, string parent, CancellationToken ct)
    {
        var queryId = cb["id"]?.ToString();
        var data = cb["data"]?.ToString();
        if (string.IsNullOrEmpty(queryId) || string.IsNullOrEmpty(data)) return;
        if (!TelegramBotClient.IsFromParent(cb["message"]?["chat"], cb["from"], parent))
        {
            await client.AnswerCallbackQueryAsync(queryId, "Bạn không có quyền.", ct);
            return;
        }
        await client.AnswerCallbackQueryAsync(queryId, null, ct);

        var parts = data.Split(':');
        string reply;
        switch (parts[0])
        {
            case "approve" when parts.Length == 3 && long.TryParse(parts[1], out var id) && int.TryParse(parts[2], out var mins):
            {
                var r = _c!.DecideExtraRequest(id, approve: true, minutes: mins);
                ServiceSignals.Wake();
                reply = (r.Success ? "✅ " : "ℹ ") + WebUtility.HtmlEncode(r.Message);
                _c.Logs.Info(LogCategory.Admin, "[Telegram] " + r.Message);
                break;
            }
            case "reject" when parts.Length == 2 && long.TryParse(parts[1], out var rid):
            {
                var r = _c!.DecideExtraRequest(rid, approve: false);
                ServiceSignals.Wake();
                reply = (r.Success ? "❌ " : "ℹ ") + WebUtility.HtmlEncode(r.Message);
                _c.Logs.Info(LogCategory.Admin, "[Telegram] " + r.Message);
                break;
            }
            case "borrow" when parts.Length == 2:
            {
                if (!_pendingBorrow.TryRemove(parts[1], out var p) || p.ExpiresUtc < DateTime.UtcNow)
                {
                    reply = "ℹ Yêu cầu xác nhận đã hết hạn. Gửi lại /muonmay nếu cần.";
                    break;
                }
                _c!.PauseProtectionFor(TimeSpan.FromMinutes(p.Minutes), "[Telegram] Phụ huynh mượn máy");
                ServiceSignals.RaiseSessionChanged();
                reply = $"🔓 <b>Đã tạm tắt bảo vệ {p.Minutes} phút</b>. Tự bật lại lúc <b>{DateTime.Now.AddMinutes(p.Minutes):HH:mm}</b> – gửi /bat để bật lại sớm.";
                break;
            }
            case "borrowcancel" when parts.Length == 2:
                _pendingBorrow.TryRemove(parts[1], out _);
                reply = "Đã hủy, bảo vệ vẫn bật.";
                break;
            default:
                return;
        }
        await client.SendMessageAsync(parent, reply, ct: ct);
    }

    private async Task HandleMessageAsync(TelegramBotClient client, JsonNode msg, string parent, CancellationToken ct)
    {
        if (!TelegramBotClient.IsFromParent(msg["chat"], msg["from"], parent)) return;
        var text = msg["text"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant().Split('@')[0];
        var c = _c!;

        switch (cmd)
        {
            case "/start":
            case "/help":
            case "/trogiup":
                await client.SendMessageAsync(parent,
                    "🤖 <b>Con Ngoan – điều khiển từ xa</b>\n\n" +
                    "• /status – thời gian dùng máy hôm nay của con\n" +
                    "• /cho 30 – cấp thêm 30 phút (5–240)\n" +
                    "• /tuchoi – từ chối các yêu cầu xin giờ đang chờ\n" +
                    "• /khoa – đưa máy của con về màn hình đăng nhập\n" +
                    "• /muonmay 30 – tạm tắt bảo vệ 30 phút để bố mẹ dùng máy (cần xác nhận)\n" +
                    "• /bat – bật lại bảo vệ ngay", ct: ct);
                break;

            case "/status":
            case "/tinhtrang":
                await client.SendMessageAsync(parent, StatusText(), ct: ct);
                break;

            case "/cho":
                if (parts.Length >= 2 && int.TryParse(parts[1], out var add) && add is >= 5 and <= 240)
                {
                    var children = c.Settings.GetChildSids();
                    foreach (var sid in children) c.GrantExtraTime(sid, add);
                    ServiceSignals.Wake();
                    c.Logs.Info(LogCategory.Admin, $"[Telegram] Phụ huynh cấp thêm {add} phút cho {children.Count} tài khoản của con.");
                    await client.SendMessageAsync(parent, children.Count == 0
                        ? "⚠ Chưa chọn tài khoản của con trong ứng dụng."
                        : $"✅ Đã cấp thêm <b>{add} phút</b> hôm nay.", ct: ct);
                }
                else
                {
                    await client.SendMessageAsync(parent, "Cách dùng: <code>/cho 30</code> (từ 5 đến 240 phút).", ct: ct);
                }
                break;

            case "/tuchoi":
            {
                var pending = c.Requests.GetPending();
                foreach (var r in pending) c.DecideExtraRequest(r.Id, approve: false);
                ServiceSignals.Wake();
                await client.SendMessageAsync(parent, pending.Count == 0
                    ? "Không có yêu cầu nào đang chờ."
                    : $"❌ Đã từ chối {pending.Count} yêu cầu.", ct: ct);
                break;
            }

            case "/khoa":
            {
                var locked = 0;
                foreach (var sid in c.Settings.GetChildSids()) locked += LockChildSessions(sid);
                c.Logs.Info(LogCategory.Admin, $"[Telegram] Phụ huynh khóa máy của con ({locked} phiên).");
                await client.SendMessageAsync(parent, locked == 0
                    ? "ℹ Con hiện không đăng nhập máy."
                    : "🔒 Đã đưa máy của con về màn hình đăng nhập.", ct: ct);
                break;
            }

            case "/muonmay":
            case "/muon":
            {
                var minutes = parts.Length >= 2 && int.TryParse(parts[1], out var m) ? Math.Clamp(m, 5, 240) : 30;
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
                _pendingBorrow[nonce] = (minutes, DateTime.UtcNow + ConfirmTtl);
                await client.SendMessageAsync(parent,
                    $"⚠ Tạm tắt <b>toàn bộ bảo vệ</b> (chặn web, ứng dụng, trình duyệt) trong <b>{minutes} phút</b>?\nHết giờ sẽ tự bật lại.",
                    [[($"Xác nhận tắt {minutes} phút", $"borrow:{nonce}"), ("Hủy", $"borrowcancel:{nonce}")]], ct);
                break;
            }

            case "/bat":
            case "/khoiphuc":
                c.ResumeProtection();
                ServiceSignals.RaiseSessionChanged();
                c.Logs.Info(LogCategory.Admin, "[Telegram] Phụ huynh bật lại bảo vệ.");
                await client.SendMessageAsync(parent, "🛡 <b>Đã bật lại toàn bộ bảo vệ.</b>", ct: ct);
                break;

            default:
                await client.SendMessageAsync(parent, "Lệnh không hỗ trợ. Gửi /trogiup để xem danh sách lệnh.", ct: ct);
                break;
        }
    }

    private string StatusText()
    {
        var c = _c!;
        var children = c.Settings.GetChildSids();
        if (children.Count == 0) return "⚠ Chưa chọn tài khoản của con trong ứng dụng.";
        var sb = new StringBuilder("📊 <b>Hôm nay</b>\n");
        if (c.PausedUntilUtc is { } until && until > DateTime.UtcNow)
            sb.AppendLine($"🔓 Bảo vệ đang tạm tắt (mượn máy) đến {until.ToLocalTime():HH:mm}.");
        else if (c.IsPaused) sb.AppendLine("🔓 Bảo vệ đang TẮT.");
        if (c.IsInBreakNow()) sb.AppendLine("☕ Đang giờ giải lao.");
        foreach (var sid in children)
        {
            var s = c.GetTimeStatus(sid, DateTime.UtcNow);
            sb.AppendLine($"\n👤 <b>{WebUtility.HtmlEncode(Label(sid))}</b>");
            sb.AppendLine($"• Đã dùng: {s.UsedSeconds / 60} phút");
            sb.AppendLine($"• Còn lại: {(s.RemainingSeconds is { } r ? $"{r / 60} phút" : "không giới hạn")}");
            sb.AppendLine($"• {WebUtility.HtmlEncode(s.Description)}");
        }
        return sb.ToString();
    }

    private async Task SendDailyReportIfDueAsync(TelegramBotClient client, string parent, CancellationToken ct)
    {
        var c = _c!;
        if (!c.Settings.GetBool(SettingKeys.TelegramDailyReport, fallback: true)) return;
        var timeText = c.Settings.Get(SettingKeys.TelegramDailyReportTime) ?? "21:00";
        if (!TimeSpan.TryParse(timeText, out var at) || at < TimeSpan.Zero || at >= TimeSpan.FromDays(1)) at = new TimeSpan(21, 0, 0);
        var now = DateTime.Now;
        var today = now.ToString("yyyy-MM-dd");
        if (now.TimeOfDay < at || c.Settings.Get(SettingKeys.TelegramDailyReportLastDate) == today) return;
        if (c.Settings.GetChildSids().Count == 0) return;

        var blocked = c.Logs.Query(new LogQuery { FromLocal = now.Date, ToLocal = now, Limit = 5000 })
            .Count(l => (l.Category == LogCategory.Application && l.Message.Contains("Đã đóng", StringComparison.Ordinal)));
        var text = $"📊 <b>Báo cáo ngày {now:dd/MM/yyyy}</b> – máy <b>{WebUtility.HtmlEncode(Environment.MachineName)}</b>\n" +
                   StatusText().Replace("📊 <b>Hôm nay</b>\n", "") +
                   $"\n🛡 Ứng dụng bị chặn đã tự đóng: <b>{blocked} lần</b>";
        var (ok, _) = await client.SendMessageAsync(parent, text, ct: ct);
        if (ok) c.Settings.Set(SettingKeys.TelegramDailyReportLastDate, today);
    }

    private static int LockChildSessions(string sid)
    {
        var count = 0;
        foreach (var s in WindowsSessions.Enumerate())
        {
            if (!string.Equals(s.Sid, sid, StringComparison.OrdinalIgnoreCase)) continue;
            if (WindowsSessions.Disconnect(s.SessionId)) count++;
        }
        return count;
    }

    private static string Label(string sid)
    {
        try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value.Split('\\').Last(); }
        catch (Exception) { return sid; }
    }
}
