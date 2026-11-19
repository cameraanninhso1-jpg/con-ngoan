using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Giám sát sự kiện khởi động tiến trình thời gian thực (Real-time Process Watcher) trên Windows:
/// - Sử dụng WMI Win32_ProcessStartTrace qua COM Interop (nhẹ, không tốn CPU, không phụ thuộc NuGet ngoài).
/// - Bắt ngay lập tức khi một ứng dụng vừa được khởi chạy (&lt; 50ms) để đóng ngay thay vì chờ quét vòng lặp.
/// - Cực kỳ hữu ích cho Windows Home (nơi không có AppLocker CSP) để ngăn con lách luật.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RealtimeProcessWatcher : IDisposable
{
    private readonly Func<IReadOnlySet<string>> _getBlockedNames;
    private readonly Func<IReadOnlySet<string>> _getChildSids;
    private readonly Action<string, int> _onProcessKilled;
    private readonly Action<string> _logError;
    private CancellationTokenSource? _cts;
    private Thread? _workerThread;
    private bool _disposed;

    public RealtimeProcessWatcher(
        Func<IReadOnlySet<string>> getBlockedNames,
        Func<IReadOnlySet<string>> getChildSids,
        Action<string, int> onProcessKilled,
        Action<string>? logError = null)
    {
        _getBlockedNames = getBlockedNames;
        _getChildSids = getChildSids;
        _onProcessKilled = onProcessKilled;
        _logError = logError ?? (_ => { });
    }

    /// <summary>Khởi động luồng giám sát sự kiện tiến trình.</summary>
    public void Start()
    {
        if (_workerThread is not null) return;
        _cts = new CancellationTokenSource();
        _workerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "CaoMinh_ProcessWatcher",
            Priority = ThreadPriority.AboveNormal
        };
        _workerThread.Start(_cts.Token);
    }

    /// <summary>Dừng giám sát sự kiện.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _workerThread = null;
    }

    private void ListenLoop(object? state)
    {
        var ct = (CancellationToken)(state ?? CancellationToken.None);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
                if (locatorType is null) throw new PlatformNotSupportedException("WbemScripting.SWbemLocator không tồn tại.");

                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic services = locator.ConnectServer(".", "root\\cimv2");
                dynamic eventSource = services.ExecNotificationQuery("SELECT * FROM Win32_ProcessStartTrace");

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // NextEvent timeout 1000ms để kiểm tra cờ hủy
                        dynamic nextEvent = eventSource.NextEvent(1000);
                        if (nextEvent is null) continue;

                        int pid = Convert.ToInt32(nextEvent.ProcessID);
                        string processName = Convert.ToString(nextEvent.ProcessName) ?? "";

                        InspectAndEnforce(pid, processName);
                    }
                    catch (COMException ex) when ((uint)ex.ErrorCode == 0x80041068) // wbemErrTimedout
                    {
                        // Hết 1 giây không có tiến trình mới sinh ra – lặp tiếp
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Bỏ qua lỗi lẻ của sự kiện và tiếp tục
                        Thread.Sleep(200);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logError("WMI ProcessStartTrace gặp lỗi: " + ex.Message + " (sẽ thử kết nối lại sau 20 giây).");
                try { Task.Delay(TimeSpan.FromSeconds(20), ct).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Kiểm tra một tiến trình vừa mở và đóng ngay nếu vi phạm.</summary>
    public bool InspectAndEnforce(int processId, string processName)
    {
        if (processId <= 4) return false;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(processName);
        if (string.IsNullOrEmpty(nameWithoutExt)) return false;

        var blockedNames = _getBlockedNames();
        if (blockedNames.Count == 0 || !blockedNames.Contains(nameWithoutExt)) return false;

        var childSids = _getChildSids();
        if (childSids.Count == 0) return false;

        // Kiểm tra chủ sở hữu tiến trình
        var ownerSid = ProcessOwner.GetOwnerSid(processId);
        if (ownerSid is null || !childSids.Contains(ownerSid)) return false;

        try
        {
            using var p = Process.GetProcessById(processId);
            if (!p.HasExited)
            {
                p.Kill();
                _onProcessKilled(processName, processId);
                return true;
            }
        }
        catch (Exception)
        {
            // Tiến trình đã thoát hoặc không có quyền truy cập
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
