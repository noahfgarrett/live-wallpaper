using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class PowerStatus
{
    private const byte OnBattery = 0;

    public static bool IsOnBattery() => Kernel32.GetSystemPowerStatus(out var status) && status.ACLineStatus == OnBattery;
}
