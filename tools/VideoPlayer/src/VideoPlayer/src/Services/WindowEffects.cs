using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoPlayer.Services;

/// <summary>
/// Win11 窗口外观微调（圆角、深色边框）。在旧系统上调用失败会被忽略。
/// </summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var dark = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var round = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

            // 0x00202830 —— 与深色界面协调的细边框
            var border = unchecked((int)0x00202830);
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch
        {
            // 非 Win11 或 DWM 不可用时忽略
        }
    }
}
