using System.Drawing;
using System.Numerics;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Rain;

namespace LiveWallpaper.Core.Tests.Rain;

public sealed class RainFrameBuilderTests
{
    private static readonly Size Target = new(1920, 1080);

    [Fact]
    public void WrapMargin_IsIndependentOfSettings()
    {
        var a = default(RainFrameConstants);
        var b = default(RainFrameConstants);
        RainFrameBuilder.Build(0, 0, 0, new RainSettings(LengthPct: 0), Target, ref a);
        RainFrameBuilder.Build(0, 0, 0, new RainSettings(LengthPct: 100, SpeedPct: 100), Target, ref b);
        Assert.Equal(a.Margin, b.Margin);
    }

    [Fact]
    public void Build_After30Days_KeepsShaderValuesBounded()
    {
        var c = default(RainFrameConstants);
        RainFrameBuilder.Build(30 * 86400 * 1.5, 1e9, 520, new RainSettings(), Target, ref c);

        var spanX = Target.Width + (2 * c.Margin);
        Assert.InRange(c.FallPhase, 0f, 0.99999994f);
        Assert.All([c.WindOffsets.X, c.WindOffsets.Y, c.WindOffsets.Z], o => Assert.InRange(o, 0f, spanX));
        Assert.All(
            [c.FallPhase, c.WindOffsets.X, c.WindOffsets.Y, c.WindOffsets.Z, c.WindPxPerSec, c.SpeedMul, c.Exposure, c.Margin, c.Scale, c.Opacity, c.Period, c.StreakCount],
            v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void Build_WindOffsetsUseLayerResponse()
    {
        var c = default(RainFrameConstants);
        RainFrameBuilder.Build(0, 100, 0, new RainSettings(), Target, ref c);
        Assert.Equal(70f, c.WindOffsets.X, 3);
        Assert.Equal(90f, c.WindOffsets.Y, 3);
        Assert.Equal(110f, c.WindOffsets.Z, 3);
    }

    [Fact]
    public void Build_FillsSettingDerivedFields()
    {
        var c = default(RainFrameConstants);
        RainFrameBuilder.Build(1800, 0, 40, new RainSettings(SpeedPct: 100, LengthPct: 100, BrightnessPct: 0, DensityPct: 100), Target, ref c);
        Assert.Equal(0.5f, c.FallPhase, 6);
        Assert.Equal(40f, c.WindPxPerSec);
        Assert.Equal(1.5f, c.SpeedMul);
        Assert.Equal(0.05f, c.Exposure, 6);
        Assert.Equal(0.4f, c.Opacity, 6);
        Assert.Equal(3600f, c.Period);
        Assert.Equal(1f, c.Scale);
        Assert.Equal(new Vector2(1920, 1080), c.Resolution);
        Assert.Equal(RainModel.StreakCount(100, 1920 * 1080 / 1e6), (int)c.StreakCount);
    }

    [Fact]
    public void ConstantsSizeIsAMultipleOf16()
    {
        Assert.Equal(0, System.Runtime.InteropServices.Marshal.SizeOf<RainFrameConstants>() % 16);
    }
}
