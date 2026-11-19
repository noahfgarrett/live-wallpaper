using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Tests.Snow;

public sealed class SnowModelTests
{
    [Theory]
    [InlineData(0, 2.0736, 0)]
    [InlineData(50, 2.0736, 1100)]
    [InlineData(100, 2.0736, 3110)]
    [InlineData(100, 33.2, 12000)]
    public void FlakeCount_MatchesPreview(double density, double megapixels, int expected) =>
        Assert.Equal(expected, SnowModel.FlakeCount(density, megapixels));

    [Theory]
    [InlineData(0, 0.25)]
    [InlineData(50, 1.0)]
    [InlineData(100, 1.75)]
    public void SpeedMultiplier_MatchesPreview(double speed, double expected) =>
        Assert.Equal(expected, SnowModel.SpeedMultiplier(speed), 9);

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(50, 1.0)]
    [InlineData(100, 1.5)]
    public void SizeMultiplier_MatchesPreview(double size, double expected) =>
        Assert.Equal(expected, SnowModel.SizeMultiplier(size), 9);

    [Fact]
    public void LayerShares_SumToOne() => Assert.Equal(1.0, SnowModel.Layers.Sum(l => (double)l.Share), 6);

    [Fact]
    public void WrapMarginPx_IsIndependentOfSize()
    {
        var largest = SnowModel.Layers.Max(l => (double)l.SizeMax) * SnowModel.SizeMultiplier(100);
        Assert.Equal((largest * 1.5) + 40, SnowModel.WrapMarginPx(1), 9);
        Assert.Equal(SnowModel.WrapMarginPx(1) * 2, SnowModel.WrapMarginPx(2), 9);
    }

    [Fact]
    public void FallClock_DoesNotJumpWhenSpeedChanges()
    {
        var clock = new FallClock();
        double t = 0;
        for (var i = 0; i < 60; i++)
        {
            t = clock.Step(1.0 / 60, 10);
        }
        Assert.True(clock.Step(1.0 / 60, 100) - t <= (1.0 / 60 * 1.75) + 1e-9);
    }

    [Fact]
    public void Wind_ZeroNeverMovesFlakesSideways()
    {
        var wind = new WindModel(42);
        for (var i = 0; i < 600; i++)
        {
            Assert.Equal(0, wind.Step(1.0 / 60, 0, 1080).OffsetPx);
        }
    }

    [Fact]
    public void Wind_OffsetChangesSmoothly()
    {
        var wind = new WindModel(7);
        var prev = wind.Step(1.0 / 60, 100, 1080);
        for (var i = 0; i < 3600; i++)
        {
            var cur = wind.Step(1.0 / 60, 100, 1080);
            Assert.True(Math.Abs(cur.OffsetPx - prev.OffsetPx) <= (Math.Abs(cur.WindPxPerSec) / 60) + 1e-6);
            prev = cur;
        }
    }

    [Fact]
    public void Wind_IsDeterministicForASeed()
    {
        var a = new WindModel(3);
        var b = new WindModel(3);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.Step(0.016, 60, 1080), b.Step(0.016, 60, 1080));
        }
    }

    [Fact]
    public void Wind_GustsVaryOverTime()
    {
        var wind = new WindModel(11);
        var speeds = new HashSet<long>();
        for (var i = 0; i < 6000; i++)
        {
            speeds.Add((long)Math.Round(wind.Step(1.0 / 60, 100, 1080).WindPxPerSec));
        }
        Assert.True(speeds.Count > 10);
    }
}
