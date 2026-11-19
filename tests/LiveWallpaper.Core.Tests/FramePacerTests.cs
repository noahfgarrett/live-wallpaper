using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Rendering;

namespace LiveWallpaper.Core.Tests;

public sealed class FramePacerTests
{
    [Theory]
    [InlineData(FrameRateMode.Fps30, 144, false, 30)]
    [InlineData(FrameRateMode.Fps60, 144, false, 60)]
    [InlineData(FrameRateMode.Fps60, 144, true, 60)]
    [InlineData(FrameRateMode.Adaptive, 144, false, 60)]
    [InlineData(FrameRateMode.Adaptive, 50, false, 50)]
    [InlineData(FrameRateMode.Adaptive, 144, true, 30)]
    [InlineData(FrameRateMode.Adaptive, 0, false, 60)]
    [InlineData(FrameRateMode.Adaptive, double.NaN, false, 60)]
    public void IntervalFor_ReturnsExpectedRate(FrameRateMode mode, double refreshHz, bool onBattery, double expectedFps)
    {
        var interval = FramePacer.IntervalFor(mode, refreshHz, onBattery);

        Assert.Equal(1.0 / expectedFps, interval.TotalSeconds, 6);
    }
}
