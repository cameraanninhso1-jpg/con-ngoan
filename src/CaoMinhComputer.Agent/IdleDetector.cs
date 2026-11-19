using System.Runtime.InteropServices;

namespace CaoMinhComputer.Agent;

/// <summary>
/// Thời gian không thao tác (chuột/bàn phím) trong phiên hiện tại.
/// Nếu có ứng dụng đang giữ màn hình sáng (ES_DISPLAY_REQUIRED – ví dụ đang phát video, họp trực tuyến) thì coi như đang dùng.
/// </summary>
internal static class IdleDetector
{
    private const int SystemExecutionState = 16;
    private const uint EsDisplayRequired = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength,
        out uint outputBuffer, uint outputBufferLength);

    public static int GetInputIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime); // số học uint xử lý tràn sau 49,7 ngày
        return (int)Math.Min(idleMs / 1000, int.MaxValue);
    }

    public static bool IsDisplayRequired()
    {
        try
        {
            return CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out var state, sizeof(uint)) == 0 &&
                   (state & EsDisplayRequired) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static int GetEffectiveIdleSeconds() => IsDisplayRequired() ? 0 : GetInputIdleSeconds();
}
