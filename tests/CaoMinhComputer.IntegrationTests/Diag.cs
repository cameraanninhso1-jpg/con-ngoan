using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CaoMinhComputer.IntegrationTests;

/// <summary>Thông tin chẩn đoán trạng thái idle/nguồn điện (dùng trong thông báo lỗi của test đầu-cuối).</summary>
internal static class Diag
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, out uint output, uint outputLength);

    public static long InputIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) / 1000 : -1;
    }

    public static bool DisplayRequired() =>
        CallNtPowerInformation(16, IntPtr.Zero, 0, out var state, 4) == 0 && (state & 0x2) != 0;

    public static string PowerRequests()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", "/requests")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
