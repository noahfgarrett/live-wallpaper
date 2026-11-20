using System.Drawing;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Glass;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Glass;

public sealed class GlassFrameBuilderTests
{
    private static readonly Size Target = new(1920, 1080);
    private static readonly RectangleF FullImage = new(0, 0, 1920, 1080);

    private static Region Quad(params (double X, double Y)[] points) =>
        new(points.Select(p => new RegionPoint(p.X, p.Y)).ToArray());

    private static RegionSet Set(params Region[] regions) => new(regions);

    [Fact]
    public void StructSizes_AreMultiplesOf16()
    {
        Assert.Equal(0, Marshal.SizeOf<GlassPane>() % 16);
        Assert.Equal(0, Marshal.SizeOf<GlassFrameConstants>() % 16);
    }

    [Fact]
    public void BuildPanes_NoRegions_CoversTheTarget()
    {
        var panes = new GlassPane[GlassModel.MaxPanes];

        var count = GlassFrameBuilder.BuildPanes(Set(), FullImage, Target, panes);

        Assert.Equal(1, count);
        Assert.Equal(new System.Numerics.Vector4(0, 0, 1920, 1080), panes[0].BoundsPx);
        Assert.Equal(new System.Numerics.Vector2(1920, 1080), panes[0].PanePx);
    }

    [Fact]
    public void BuildPanes_SkipsDegeneratePane_KeepsOthers()
    {
        var degenerate = Quad((0, 0), (0.5, 0.5), (1, 1), (0.2, 0.2));
        var good = Quad((0.1, 0.1), (0.5, 0.1), (0.5, 0.5), (0.1, 0.5));
        var panes = new GlassPane[GlassModel.MaxPanes];

        var count = GlassFrameBuilder.BuildPanes(Set(degenerate, good), FullImage, Target, panes);

        Assert.Equal(1, count);
        Assert.Equal(192f, panes[0].BoundsPx.X);
    }

    [Fact]
    public void BuildPanes_PanePxIsAverageEdgeLength()
    {
        var bounds = new RectangleF(0, 0, 1000, 1000);
        var quad = Quad((0, 0), (0.4, 0), (0.5, 0.2), (0, 0.2));
        var panes = new GlassPane[GlassModel.MaxPanes];

        GlassFrameBuilder.BuildPanes(Set(quad), bounds, Target, panes);

        var top = 400.0;
        var bottom = Math.Sqrt((500 * 500) + 0);
        var left = 200.0;
        var right = Math.Sqrt((100 * 100) + (200 * 200));
        Assert.Equal((top + bottom) / 2, panes[0].PanePx.X, 1e-3);
        Assert.Equal((left + right) / 2, panes[0].PanePx.Y, 1e-3);
    }

    [Fact]
    public void BuildPanes_ClampsBoundsToTarget()
    {
        var quad = Quad((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5));
        var panes = new GlassPane[GlassModel.MaxPanes];

        GlassFrameBuilder.BuildPanes(Set(quad), FullImage, Target, panes);

        Assert.Equal(new System.Numerics.Vector4(0, 0, 960, 540), panes[0].BoundsPx);
    }

    [Fact]
    public void BuildFrame_After30Days_TimePhaseInUnitRange()
    {
        var c = default(GlassFrameConstants);

        GlassFrameBuilder.BuildFrame(30 * 24 * 3600.0 + 123.456, new GlassSettings(), Target, 1, ref c);

        Assert.InRange(c.TimePhase, 0f, 1f);
    }

    [Fact]
    public void BuildFrame_MapsSettings()
    {
        var c = default(GlassFrameConstants);
        var settings = new GlassSettings(true, DropsPct: 50, DropSizePct: 100, SlidingPct: 40, FogPct: 30);

        GlassFrameBuilder.BuildFrame(0, settings, Target, 3, ref c);

        Assert.Equal(0.5f, c.Drops);
        Assert.Equal(1.4f, c.SizeMul);
        Assert.Equal(0.4f, c.Sliding);
        Assert.Equal(0.3f, c.Fog);
        Assert.Equal(3f, c.PaneCount);
        Assert.Equal(1f, c.Scale);
    }
}
