using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Rendering;

public static class FramePacer
{
    private const double MaxFps = 60;
    private const double BatteryFps = 30;

    public static TimeSpan IntervalFor(FrameRateMode mode, double refreshHz, bool onBattery)
    {
        var fps = mode switch
        {
            FrameRateMode.Fps30 => 30,
            FrameRateMode.Fps60 => 60,
            FrameRateMode.Adaptive when onBattery => BatteryFps,
            FrameRateMode.Adaptive => double.IsFinite(refreshHz) && refreshHz > 0 ? Math.Min(refreshHz, MaxFps) : MaxFps,
            _ => MaxFps,
        };
        return TimeSpan.FromSeconds(1.0 / fps);
    }
}
