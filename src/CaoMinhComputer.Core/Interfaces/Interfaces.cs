using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Interfaces;

/// <summary>
/// Cơ chế chặn website. Bản MVP dùng HOSTS (<see cref="Services.HostsBlockingProvider"/>);
/// có thể thay bằng provider WFP/DNS filtering ở phiên bản sau mà không đổi UI.
/// </summary>
public interface IWebsiteBlockingProvider
{
    string Name { get; }

    /// <summary>Thay toàn bộ khối do Con Ngoan quản lý bằng danh sách mới (rỗng = gỡ khối).</summary>
    ApplyResult Apply(IReadOnlyCollection<string> domains);

    /// <summary>Gỡ mọi thay đổi do Con Ngoan tạo, giữ nguyên nội dung khác.</summary>
    ApplyResult RemoveAll();

    /// <summary>Danh sách tên miền hiện đang được áp dụng.</summary>
    IReadOnlyList<string> GetAppliedDomains();

    /// <summary>Kiểm tra trạng thái thực tế có khớp với cấu hình mong muốn hay không.</summary>
    bool IsInSync(IReadOnlyCollection<string> expectedDomains);
}

/// <summary>Lưu giá trị gốc của các thiết lập hệ thống trước khi Con Ngoan thay đổi, để khôi phục chính xác.</summary>
public interface ISystemChangeStore
{
    bool TryGetOriginal(string key, out bool existed, out string? value);

    /// <summary>Chỉ lưu nếu chưa có bản gốc (lần thay đổi đầu tiên).</summary>
    void SaveOriginalIfAbsent(string key, bool existed, string? value);

    void Remove(string key);

    IReadOnlyList<string> Keys();
}
