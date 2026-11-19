using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>Phạm vi chặn website.</summary>
public enum WebBlockScope
{
    /// <summary>Chỉ chặn khi tài khoản của con đang dùng máy (phụ huynh vẫn vào được).</summary>
    Children,

    /// <summary>Chặn cho mọi tài khoản trên máy.</summary>
    All
}

/// <summary>Quyết định có chặn website tại thời điểm hiện tại hay không.</summary>
public sealed record WebScopeDecision(bool Block, string Reason);

/// <summary>
/// HOSTS áp dụng cho cả máy, nhưng Windows bản client (Home/Pro) chỉ có MỘT phiên người dùng hoạt động tại một thời điểm
/// (chuyển người dùng/Remote Desktop sẽ ngắt phiên kia). Vì vậy chặn theo "phiên đang hoạt động" tương đương chặn theo tài khoản:
/// - Phiên của con đang hoạt động → chặn.
/// - Phiên của tài khoản khác (phụ huynh) đang hoạt động → không chặn.
/// - Chưa có ai đăng nhập/không xác định → chặn (an toàn mặc định).
/// - Chưa đánh dấu tài khoản con, hoặc phụ huynh chọn "mọi tài khoản" → luôn chặn.
/// </summary>
public static class WebScopePolicy
{
    public static WebScopeDecision Decide(WebBlockScope scope, IReadOnlyCollection<string> childSids, IReadOnlyList<UserSession> sessions)
    {
        if (scope == WebBlockScope.All)
            return new(true, "phụ huynh chọn chặn cho mọi tài khoản");
        if (childSids.Count == 0)
            return new(true, "chưa đánh dấu tài khoản của con nên chặn cho mọi tài khoản");

        var active = sessions.Where(s => s.IsActive).ToList();
        if (active.Count == 0)
            return new(true, "chưa có ai đăng nhập");

        if (active.Any(s => s.Sid is null || childSids.Contains(s.Sid, StringComparer.OrdinalIgnoreCase)))
            return new(true, $"tài khoản của con ({string.Join(", ", active.Select(s => s.UserName))}) đang dùng máy");

        return new(false, $"tài khoản đang dùng máy ({string.Join(", ", active.Select(s => s.UserName))}) không phải tài khoản của con");
    }
}
