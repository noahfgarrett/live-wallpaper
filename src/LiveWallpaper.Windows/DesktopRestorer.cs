using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static unsafe class DesktopRestorer
{
    private const int MaxPath = 32768;

    public static void RestoreDesktop()
    {
        Redraw();
        var slideshow = SafeIsSlideshowActive();
        if (slideshow != false)
        {
            Log.Info("desktop", $"event=restore method=redraw slideshow={slideshow?.ToString() ?? "unknown"}");
            return;
        }

        var buffer = new char[MaxPath];
        fixed (char* p = buffer)
        {
            if (!User32.SystemParametersInfo(User32.SPI_GETDESKWALLPAPER, MaxPath, p, 0))
            {
                Log.Error("desktop", "event=restore-read-failed");
                return;
            }
            var ok = User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, p, 0);
            Log.Info("desktop", $"event=restore method=redraw+reapply repainted={ok}");
        }
    }

    private static void Redraw()
    {
        const uint flags = User32.RDW_INVALIDATE | User32.RDW_ERASE | User32.RDW_ALLCHILDREN | User32.RDW_UPDATENOW;
        foreach (var hwnd in WindowEnumeration.TopLevel())
        {
            var className = User32.GetClassName(hwnd);
            if (className is DesktopLayoutClassifier.ProgmanClass or DesktopLayoutClassifier.WorkerWClass)
            {
                User32.RedrawWindow(hwnd, 0, 0, flags);
            }
        }
    }

    private static bool? SafeIsSlideshowActive()
    {
        try
        {
            return DesktopWallpaperCom.IsSlideshowActive();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            Log.Error("desktop", "event=slideshow-status-failed", ex);
            return null;
        }
    }
}
