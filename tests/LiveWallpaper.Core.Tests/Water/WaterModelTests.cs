using System.Drawing;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Water;

namespace LiveWallpaper.Core.Tests.Water;

public sealed class WaterModelTests
{
    [Fact]
    public void SpeedPxPerSec_DefaultArrowOnSquareImage()
    {
        var (x, y) = WaterModel.SpeedPxPerSec(new RegionPoint(0, 0.12), new SizeF(1000, 1000));

        Assert.Equal(0, x, 6);
        Assert.Equal(30, y, 6);
    }

    [Fact]
    public void SpeedPxPerSec_SameScreenLengthSameSpeed()
    {
        var size = new SizeF(2000, 1000);

        var (hx, hy) = WaterModel.SpeedPxPerSec(new RegionPoint(0.06, 0), size);
        var (vx, vy) = WaterModel.SpeedPxPerSec(new RegionPoint(0, 0.12), size);

        Assert.Equal(Math.Sqrt((hx * hx) + (hy * hy)), Math.Sqrt((vx * vx) + (vy * vy)), 6);
    }

    [Fact]
    public void SpeedPxPerSec_ZeroVector_IsZero()
    {
        Assert.Equal((0.0, 0.0), WaterModel.SpeedPxPerSec(new RegionPoint(0, 0), new SizeF(1000, 1000)));
    }

    [Fact]
    public void ClampArrow_ZeroBecomesMinimumDown()
    {
        Assert.Equal(new RegionPoint(0, WaterModel.MinArrow), WaterModel.ClampArrow(new RegionPoint(0, 0), 1.5));
    }

    [Fact]
    public void ClampArrow_TooLongIsShortened()
    {
        var clamped = WaterModel.ClampArrow(new RegionPoint(0.9, 0), 2);

        Assert.Equal(WaterModel.MaxArrow, Math.Abs(clamped.X * 2), 6);
        Assert.Equal(0, clamped.Y, 6);
    }

    [Fact]
    public void ClampArrow_TooShortIsLengthened()
    {
        var clamped = WaterModel.ClampArrow(new RegionPoint(0, 0.001), 1);

        Assert.Equal(WaterModel.MinArrow, clamped.Y, 6);
    }

    [Fact]
    public void ClampArrow_InRangeIsUnchanged()
    {
        Assert.Equal(new RegionPoint(0.05, 0.1), WaterModel.ClampArrow(new RegionPoint(0.05, 0.1), 1));
    }

    [Fact]
    public void CycleSeconds_SpansHalfToThree()
    {
        Assert.Equal(0.5, WaterModel.CycleSeconds(0), 9);
        Assert.Equal(3, WaterModel.CycleSeconds(100), 9);
    }

    [Fact]
    public void MistReach_MatchesModel()
    {
        Assert.Equal(0.04 * 1000, WaterModel.MistReachPx(0, 1000), 6);
        Assert.Equal(0.14 * 1000, WaterModel.MistReachPx(100, 1000), 6);
        Assert.Equal(WaterModel.MaxMistReachFraction, WaterModel.MistReachPx(100, 1), 9);
    }

    [Fact]
    public void PhaseClock_StaysInUnitRange_After30Days()
    {
        var clock = new WaterPhaseClock();
        var phase = clock.Step(30 * 24 * 3600.0, 50);
        Assert.True(phase is >= 0 and < 1);

        for (var i = 0; i < 600; i++)
        {
            phase = clock.Step(1.0 / 60, 50);
            Assert.True(phase is >= 0 and < 1);
        }
    }

    [Fact]
    public void PhaseClock_StretchChangeDoesNotJump()
    {
        var clock = new WaterPhaseClock();
        var before = clock.Step(0.7, 0);

        var after = clock.Step(0.1, 100);

        Assert.Equal(0.1 / WaterModel.CycleSeconds(100), after - before, 9);
    }

    [Fact]
    public void FrameConstants_SizeIsAMultipleOf16()
    {
        Assert.Equal(0, Marshal.SizeOf<WaterFrameConstants>() % 16);
    }

    [Fact]
    public void FrameBuilder_FillsConstantsFromSettings()
    {
        var c = default(WaterFrameConstants);
        var dest = new RectangleF(100, 0, 1600, 900);

        WaterFrameBuilder.Build(0.25, new WaterSettings(true, 100, 30, 50), dest, new Size(1920, 1080), ref c);

        Assert.Equal(0.25f, c.Phase);
        Assert.Equal(3f, c.Cycle);
        Assert.Equal(0.3f, c.Foam, 5);
        Assert.Equal(0.5f, c.Mist, 5);
        Assert.Equal(1f, c.Scale, 5);
        Assert.Equal((float)WaterModel.MistReachPx(50, 900), c.MistReach, 3);
        Assert.Equal(100f, c.ImageDest.X);
        Assert.Equal(1600f, c.ImageDest.Z);
    }
}
