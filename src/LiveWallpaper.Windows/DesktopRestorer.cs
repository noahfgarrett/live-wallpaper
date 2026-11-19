using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static unsafe class DesktopRestorer
{
    private const int MaxPath = 32768;

    public static void RepaintWallpaper()
    {
        var buffer = new char[MaxPath];
        fixed (char* p = buffer)
        {
            if (!User32.SystemParametersInfo(User32.SPI_GETDESKWALLPAPER, MaxPath, p, 0))
            {
                Log.Error("desktop", "event=restore-read-failed");
                return;
            }
            var ok = User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, p, 0);
            Log.Info("desktop", $"event=restore repainted={ok}");
        }
    }
}
