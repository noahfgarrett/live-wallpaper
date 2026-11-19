using LiveWallpaper.Core.Rain;
using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Tests.Rain;

public sealed class RainModelTests
{
    [Fact]
    public void StreakCount_FollowsTheCurveAndCap()
    {
        Assert.Equal((int)Math.Round(1400 * Math.Pow(0.5, 1.4) * 2.0736, MidpointRounding.AwayFromZero), RainModel.StreakCount(50, 2.0736));
        Assert.Equal(9000, RainModel.StreakCount(100, 50));
    }

    [Fact]
    public void StreakCount_TreatsNonFiniteDensityAsZero()
    {
        Assert.Equal(0, RainModel.StreakCount(double.NaN, 2));
    }

    [Fact]
    public void Multipliers_MatchTheModel()
    {
        Assert.Equal(0.5, RainModel.SpeedMultiplier(0), 9);
        Assert.Equal(1.5, RainModel.SpeedMultiplier(100), 9);
        Assert.Equal(0.008, RainModel.ExposureSeconds(0), 9);
        Assert.Equal(0.05, RainModel.ExposureSeconds(100), 9);
        Assert.Equal(0.4, RainModel.OpacityMultiplier(0), 9);
        Assert.Equal(2.0, RainModel.OpacityMultiplier(100), 9);
    }

    [Fact]
    public void WrapMargin_MatchesFormulaAndScales()
    {
        Assert.Equal((2500 * 1.5 * 0.05 * 1.2) + 49, RainModel.WrapMarginPx(1), 9);
        Assert.Equal(2 * RainModel.WrapMarginPx(1), RainModel.WrapMarginPx(2), 9);
    }

    [Fact]
    public void WindBase_IsRainsOwn()
    {
        var snow = new WindModel(1).Step(1, 100, 1080).WindPxPerSec;
        var rain = new WindModel(1, RainModel.WindBaseAtFull).Step(1, 100, 1080).WindPxPerSec;
        Assert.Equal(520.0 / 120.0, rain / snow, 9);
    }

    [Fact]
    public void FallClock_StepScaledAppliesTheGivenMultiplier()
    {
        var clock = new FallClock();
        Assert.Equal(3.0, clock.StepScaled(2, 1.5), 9);
        Assert.Equal(3.0, clock.StepScaled(-1, 1.5), 9);
    }
}
