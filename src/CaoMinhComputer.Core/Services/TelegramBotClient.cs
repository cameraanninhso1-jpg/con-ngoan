using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Client tương tác với Telegram Bot API nhẹ nhàng, không phụ thuộc thư viện ngoài.
/// Dùng để gửi thông báo cho phụ huynh và nhận lệnh điều khiển từ xa.
/// </summary>
public sealed class TelegramBotClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _baseUrl;

    public TelegramBotClient(string token, HttpClient? httpClient = null)
    {
        _token = token.Trim();
        _baseUrl = $"https://api.telegram.org/bot{_token}/";
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
    }

    /// <summary>Kiểm tra token có hợp lệ không và lấy tên bot.</summary>
    public async Task<(bool ok, string? botName, string? error)> TestTokenAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetStringAsync(_baseUrl + "getMe", ct);
            using var doc = JsonDocument.Parse(res);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                var result = doc.RootElement.GetProperty("result");
                var username = result.GetProperty("username").GetString();
                var firstName = result.TryGetProperty("first_name", out var fn) ? fn.GetString() : "";
                return (true, $"{firstName} (@{username})", null);
            }
            return (false, null, "Token không hợp lệ.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Gửi tin nhắn văn bản (HTML) kèm nút bấm tùy chọn tới Chat ID.</summary>
    public async Task<(bool ok, string? error)> SendMessageAsync(
        string chatId,
        string htmlText,
        List<List<(string text, string callbackData)>>? inlineKeyboard = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new JsonObject
            {
                ["chat_id"] = chatId.Trim(),
                ["text"] = htmlText,
                ["parse_mode"] = "HTML"
            };

            if (inlineKeyboard is { Count: > 0 })
            {
                var rowsArray = new JsonArray();
                foreach (var row in inlineKeyboard)
                {
                    var buttonsArray = new JsonArray();
                    foreach (var (text, callbackData) in row)
                    {
                        buttonsArray.Add(new JsonObject
                        {
                            ["text"] = text,
                            ["callback_data"] = callbackData
                        });
                    }
                    rowsArray.Add(buttonsArray);
                }
                payload["reply_markup"] = new JsonObject { ["inline_keyboard"] = rowsArray };
            }

            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync(_baseUrl + "sendMessage", content, ct);
            var resJson = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(resJson);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return (true, null);

            var desc = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "Lỗi gửi tin nhắn.";
            return (false, desc);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Gửi ảnh chụp màn hình (byte array JPEG) tới Chat ID qua Telegram Bot API.</summary>
    public async Task<(bool ok, string? error)> SendPhotoAsync(
        string chatId,
        byte[] photoBytes,
        string? caption = null,
        CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chatId.Trim()), "chat_id");
            if (!string.IsNullOrEmpty(caption))
            {
                content.Add(new StringContent(caption), "caption");
                content.Add(new StringContent("HTML"), "parse_mode");
            }
            var imageContent = new ByteArrayContent(photoBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "photo", "screen.jpg");

            var res = await _http.PostAsync(_baseUrl + "sendPhoto", content, ct);
            var resJson = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(resJson);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return (true, null);

            var desc = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "Lỗi gửi ảnh.";
            return (false, desc);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Phản hồi callback_query để Telegram tắt spinner trên nút bấm.</summary>
    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default)
    {
        try
        {
            var payload = new JsonObject { ["callback_query_id"] = callbackQueryId };
            if (!string.IsNullOrEmpty(text)) payload["text"] = text;
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            await _http.PostAsync(_baseUrl + "answerCallbackQuery", content, ct);
        }
        catch (Exception)
        {
            // Bỏ qua lỗi phản hồi giao diện
        }
    }

    /// <summary>Lấy danh sách updates mới nhất (Long Polling).</summary>
    public async Task<JsonNode?> GetUpdatesAsync(long offset, int timeoutSeconds = 20, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}getUpdates?offset={offset}&timeout={timeoutSeconds}&allowed_updates=[\"message\",\"callback_query\"]";
            var res = await _http.GetStringAsync(url, ct);
            return JsonNode.Parse(res);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Chỉ chấp nhận tin nhắn RIÊNG (chat private) gửi từ đúng tài khoản Telegram của phụ huynh.
    /// Tin trong nhóm/kênh bị bỏ qua, kể cả khi Chat ID trùng, để thành viên khác trong nhóm không ra lệnh được.
    /// </summary>
    public static bool IsFromParent(JsonNode? chat, JsonNode? from, string parentChatId) =>
        !string.IsNullOrEmpty(parentChatId) &&
        chat?["type"]?.ToString() == "private" &&
        chat["id"]?.ToString() == parentChatId &&
        from?["id"]?.ToString() == parentChatId;

    public void Dispose()
    {
        _http.Dispose();
    }
}
