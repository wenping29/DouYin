using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VideoPlayer.Services;

/// <summary>
/// 获取窗口所在显示器的边界（用于全屏）。
/// </summary>
public static class ScreenHelper
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>返回以 DIP 为单位的显示器边界。</summary>
    public static Rect GetMonitorBounds(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
                var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

                return new Rect(
                    info.Monitor.Left / scaleX,
                    info.Monitor.Top / scaleY,
                    (info.Monitor.Right - info.Monitor.Left) / scaleX,
                    (info.Monitor.Bottom - info.Monitor.Top) / scaleY);
            }
        }
        catch
        {
            // 回退到主屏
        }

        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }
}
