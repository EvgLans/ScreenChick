using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ScreenChick.Native;

namespace ScreenChick.Capture;

public sealed record MonitorSnapshot(BitmapSource Image, PixelBounds Bounds, double UiScale);

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

internal static class ScreenCapture
{
    /// <summary>Freezes the monitor the cursor is on.</summary>
    public static MonitorSnapshot CaptureMonitorUnderCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var bounds = MonitorBoundsAt(cursor);
        var scale = MonitorScaleAt(cursor);
        return new MonitorSnapshot(CaptureRect(bounds), bounds, scale);
    }

    private static PixelBounds MonitorBoundsAt(NativeMethods.POINT p)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
        var b = screen.Bounds;
        return new PixelBounds(b.X, b.Y, b.Width, b.Height);
    }

    private static double MonitorScaleAt(NativeMethods.POINT p)
    {
        var monitor = NativeMethods.MonitorFromPoint(p, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0
            ? dpi / 96.0
            : 1.0;
    }

    private static BitmapSource CaptureRect(PixelBounds r)
    {
        var screenDc = NativeMethods.GetDC(0);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        var previous = NativeMethods.SelectObject(memDc, bitmap);
        try
        {
            NativeMethods.BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            NativeMethods.SelectObject(memDc, previous);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }
}
