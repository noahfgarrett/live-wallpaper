using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests;

public sealed class EffectSettingsTests
{
    private static Region Quad() => RegionGeometry.CreateQuad();

    private static Region Triangle() => new([new(0.1, 0.1), new(0.5, 0.1), new(0.3, 0.5)]);

    [Fact]
    public void Defaults_MatchTheSpec()
    {
        Assert.Equal(new RainSettings(false, 50, 50, 20, 50, 50), new RainSettings());
        Assert.Equal(new GlassSettings(false, 50, 50, 40, 30), new GlassSettings());
        Assert.Equal(new WaterSettings(false, 50, 30), new WaterSettings());
        Assert.True(new SnowSettings().Enabled);
        Assert.Equal(new RainSettings(), RainSlot.Default.Settings);
        Assert.Equal(new GlassSettings(), GlassSlot.Default.Settings);
        Assert.Equal(new WaterSettings(), WaterSlot.Default.Settings);
        Assert.Equal(RegionSet.Empty, RainSlot.Default.Regions);
        Assert.Equal(RegionSet.Empty, GlassSlot.Default.Regions);
        Assert.Equal(RegionSet.Empty, WaterSlot.Default.Regions);
        Assert.Equal(new RegionPoint(0, 0.12), EffectRules.DefaultFlow);
        Assert.True(new RainSettings().IsValid);
        Assert.True(new GlassSettings().IsValid);
        Assert.True(new WaterSettings().IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void IsValid_RejectsOutOfRangeAndNaN(double bad)
    {
        Assert.False(new RainSettings(DensityPct: bad).IsValid);
        Assert.False(new RainSettings(SpeedPct: bad).IsValid);
        Assert.False(new RainSettings(WindPct: bad).IsValid);
        Assert.False(new RainSettings(LengthPct: bad).IsValid);
        Assert.False(new RainSettings(BrightnessPct: bad).IsValid);
        Assert.False(new GlassSettings(DropsPct: bad).IsValid);
        Assert.False(new GlassSettings(DropSizePct: bad).IsValid);
        Assert.False(new GlassSettings(SlidingPct: bad).IsValid);
        Assert.False(new GlassSettings(FogPct: bad).IsValid);
        Assert.False(new WaterSettings(StretchPct: bad).IsValid);
        Assert.False(new WaterSettings(FoamPct: bad).IsValid);
    }

    [Fact]
    public void Repair_Glass_DropsNonQuadsAndClearsInvert()
    {
        var set = new RegionSet([Quad(), Triangle()], Invert: true);

        var repaired = EffectRules.Repair(EffectKind.Glass, set);

        Assert.Equal([Quad()], repaired.Regions);
        Assert.False(repaired.Invert);
    }

    [Fact]
    public void Repair_Water_GivesMissingFlowTheDefaultArrow()
    {
        var withFlow = Triangle() with { Flow = new RegionPoint(0.2, 0) };
        var set = new RegionSet([Triangle(), withFlow], Invert: true);

        var repaired = EffectRules.Repair(EffectKind.Water, set);

        Assert.Equal(EffectRules.DefaultFlow, repaired.Regions[0].Flow);
        Assert.Equal(new RegionPoint(0.2, 0), repaired.Regions[1].Flow);
        Assert.False(repaired.Invert);
    }

    [Theory]
    [InlineData(5, 0, 1, 0)]
    [InlineData(-3, 4, -0.6, 0.8)]
    [InlineData(0.001, 0, 0.002, 0)]
    public void Repair_Water_ClampsOutOfRangeFlowKeepingDirection(double x, double y, double expectedX, double expectedY)
    {
        var set = new RegionSet([Triangle() with { Flow = new RegionPoint(x, y) }]);

        var flow = EffectRules.Repair(EffectKind.Water, set).Regions[0].Flow ?? default;

        Assert.True(EffectRules.IsValidFlow(flow));
        Assert.Equal(expectedX, flow.X, 6);
        Assert.Equal(expectedY, flow.Y, 6);
    }

    [Fact]
    public void Repair_Water_ZeroFlowGetsTheDefaultArrow()
    {
        var set = new RegionSet([Triangle() with { Flow = new RegionPoint(0, 0) }]);

        Assert.Equal(EffectRules.DefaultFlow, EffectRules.Repair(EffectKind.Water, set).Regions[0].Flow);
    }

    [Fact]
    public void Repair_Water_LeavesNonFiniteFlowToBeRejected()
    {
        var set = new RegionSet([Triangle() with { Flow = new RegionPoint(double.PositiveInfinity, 0) }]);

        Assert.False(EffectRules.IsValid(EffectKind.Water, EffectRules.Repair(EffectKind.Water, set)));
    }

    [Theory]
    [InlineData(EffectKind.Snow)]
    [InlineData(EffectKind.Rain)]
    public void Repair_SnowAndRain_ReturnTheSameInstance(EffectKind kind)
    {
        var set = new RegionSet([Triangle()], Invert: true);

        Assert.Same(set, EffectRules.Repair(kind, set));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(double.PositiveInfinity, 0, false)]
    [InlineData(double.NaN, 0.1, false)]
    [InlineData(1.5, 0, false)]
    [InlineData(0.0001, 0, false)]
    [InlineData(0, 0.12, true)]
    public void IsValid_Water_RejectsZeroAndInfiniteFlow(double x, double y, bool expected)
    {
        var set = new RegionSet([Triangle() with { Flow = new RegionPoint(x, y) }]);

        Assert.Equal(expected, EffectRules.IsValid(EffectKind.Water, set));
    }

    [Fact]
    public void IsValid_Water_RejectsMissingFlow() =>
        Assert.False(EffectRules.IsValid(EffectKind.Water, new RegionSet([Triangle()])));

    [Fact]
    public void IsValid_Glass_RequiresQuadsAndNoInvert()
    {
        Assert.True(EffectRules.IsValid(EffectKind.Glass, new RegionSet([Quad()])));
        Assert.False(EffectRules.IsValid(EffectKind.Glass, new RegionSet([Triangle()])));
        Assert.False(EffectRules.IsValid(EffectKind.Glass, new RegionSet([Quad()], Invert: true)));
    }
}
