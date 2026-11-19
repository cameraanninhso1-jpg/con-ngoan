using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaoMinhComputer.Service;

/// <summary>Tín hiệu đánh thức các worker theo sự kiện (thay cho polling dày).</summary>
public static class ServiceSignals
{
    /// <summary>Đăng nhập/đăng xuất/khóa/mở khóa phiên, resume, hoặc giao diện yêu cầu áp dụng lại.</summary>
    public static readonly SemaphoreSlim TimeWake = new(0);

    private static long _resumeCount;

    /// <summary>Tăng mỗi khi máy thức dậy từ sleep/hibernate – worker bỏ khoảng thời gian ngủ khỏi thời gian sử dụng.</summary>
    public static long ResumeCount => Interlocked.Read(ref _resumeCount);

    public static void Wake()
    {
        if (TimeWake.CurrentCount < 4) TimeWake.Release();
    }

    /// <summary>Đăng nhập/chuyển người dùng/khóa/mở khóa – ProtectionWorker áp dụng lại chặn website theo phiên đang hoạt động.</summary>
    public static event Action? SessionChanged;

    public static void RaiseSessionChanged()
    {
        Wake();
        SessionChanged?.Invoke();
    }

    public static void Resumed()
    {
        Interlocked.Increment(ref _resumeCount);
        Wake();
    }

    /// <summary>Sự kiện khi con gửi yêu cầu xin thêm giờ từ Agent.</summary>
    public static event Action<long, string, int, string?>? ExtraRequestSubmitted;

    public static void RaiseExtraRequestSubmitted(long requestId, string childSid, int minutes, string? reason)
    {
        ExtraRequestSubmitted?.Invoke(requestId, childSid, minutes, reason);
    }

    /// <summary>Thông báo ngắn cho phụ huynh (TelegramWorker chuyển tiếp nếu phụ huynh bật Telegram).</summary>
    public static event Action<string>? ParentNotice;

    public static void RaiseParentNotice(string text) => ParentNotice?.Invoke(text);
}

/// <summary>
/// Mở rộng vòng đời Windows Service để nhận sự kiện phiên (SessionChange) và nguồn điện (PowerEvent).
/// </summary>
public sealed class CmcServiceLifetime : WindowsServiceLifetime
{
    public CmcServiceLifetime(IHostEnvironment environment, IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory, IOptions<HostOptions> optionsAccessor, IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        CanHandleSessionChangeEvent = true;
        CanHandlePowerEvent = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        ServiceSignals.RaiseSessionChanged();
        base.OnSessionChange(changeDescription);
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        if (powerStatus is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend)
            ServiceSignals.Resumed();
        return base.OnPowerEvent(powerStatus);
    }
}
