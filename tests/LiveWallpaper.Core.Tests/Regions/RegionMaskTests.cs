using System.Drawing;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class RegionMaskTests
{
    private static readonly Size Wide = new(100, 20);
    private static readonly Size Square = new(100, 100);

    private static Region Clockwise(double left, double top, double right, double bottom) =>
        new([new(left, top), new(right, top), new(right, bottom), new(left, bottom)]);

    private static Region CounterClockwise(double left, double top, double right, double bottom) =>
        new([new(left, top), new(left, bottom), new(right, bottom), new(right, top)]);

    private static MaskImage Build(Size target, double featherPct, bool invert, params Region[] regions) =>
        Build(target, new RectangleF(0, 0, target.Width, target.Height), featherPct, invert, regions);

    private static MaskImage Build(Size target, RectangleF imageBounds, double featherPct, bool invert, params Region[] regions)
    {
        var mask = RegionMask.Build(new RegionSet(regions, invert, featherPct), imageBounds, target);
        Assert.NotNull(mask);
        return mask;
    }

    private static byte At(MaskImage mask, int x, int y) => mask.Pixels[(y * mask.Width) + x];

    private static int TransitionWidth(MaskImage mask, int y, int fromX, int toX)
    {
        var count = 0;
        for (var x = fromX; x < toX; x++)
        {
            if (At(mask, x, y) is > 10 and < 245)
            {
                count++;
            }
        }
        return count;
    }

    [Fact]
    public void Build_NoRegions_ReturnsNull() =>
        Assert.Null(RegionMask.Build(RegionSet.Empty, new RectangleF(0, 0, 100, 100), Square));

    [Fact]
    public void Build_Square_FillsInsideOnly()
    {
        var mask = Build(Square, 0, false, Clockwise(0.2, 0.2, 0.6, 0.6));

        Assert.Equal((100, 100), (mask.Width, mask.Height));
        Assert.Equal(255, At(mask, 30, 30));
        Assert.Equal(0, At(mask, 70, 70));
        Assert.Equal(0, At(mask, 10, 30));
    }

    [Fact]
    public void Build_HalfPixelEdge_IsHalfCovered()
    {
        var mask = Build(Wide, 0, false, Clockwise(0.505, 0, 1, 1));

        Assert.InRange(At(mask, 50, 10), 125, 131);
        Assert.Equal(255, At(mask, 51, 10));
        Assert.Equal(255, At(mask, 99, 10));
        Assert.Equal(0, At(mask, 49, 10));
    }

    [Fact]
    public void Build_Inverted_CutsHoles()
    {
        var mask = Build(Square, 0, true, Clockwise(0.2, 0.2, 0.6, 0.6));

        Assert.Equal(0, At(mask, 30, 30));
        Assert.Equal(255, At(mask, 70, 70));
    }

    [Fact]
    public void Build_OverlappingOppositeWinding_IsUnion()
    {
        var mask = Build(Square, 0, false, Clockwise(0.2, 0.2, 0.6, 0.8), CounterClockwise(0.4, 0.2, 0.8, 0.8));

        Assert.Equal(255, At(mask, 50, 50));
        Assert.Equal(255, At(mask, 30, 50));
        Assert.Equal(255, At(mask, 70, 50));
    }

    [Fact]
    public void Build_Feather_SoftensEdge()
    {
        var mask = Build(Wide, 2, false, Clockwise(0.5, 0, 1, 1));

        for (var x = 1; x < mask.Width; x++)
        {
            Assert.True(At(mask, x, 10) >= At(mask, x - 1, 10), $"value drops at x={x}");
        }
        Assert.InRange(At(mask, 49, 10), 80, 175);
        Assert.True(At(mask, 44, 10) < 15);
        Assert.True(At(mask, 55, 10) > 240);
        Assert.Equal(255, At(mask, 99, 10));
    }

    [Fact]
    public void Build_FeatherAtFrameEdge_StaysSolid()
    {
        var mask = Build(Wide, 2, false, Clockwise(0, 0, 0.5, 1));

        Assert.Equal(255, At(mask, 0, 10));
        Assert.Equal(255, At(mask, 0, 0));
    }

    [Fact]
    public void Build_FeatherUsesImageWidthNotTargetWidth()
    {
        var narrowImage = Build(Wide, new RectangleF(25, 0, 50, 20), 2, false, Clockwise(0.5, 0, 1, 1));
        var fullImage = Build(Wide, 2, false, Clockwise(0.5, 0, 1, 1));

        Assert.True(TransitionWidth(narrowImage, 10, 40, 60) < TransitionWidth(fullImage, 10, 40, 60));
    }

    [Fact]
    public void Build_WideFeather_DownscalesMask()
    {
        var mask = Build(new Size(1000, 500), 10, false, Clockwise(0.2, 0.2, 0.6, 0.6));

        Assert.Equal((125, 63), (mask.Width, mask.Height));
        Assert.True(At(mask, 50, 25) > 240);
        Assert.True(At(mask, 120, 60) < 15);
    }

    [Fact]
    public void ScaleFor_KeepsSmallBlursAtFullResolution()
    {
        Assert.Equal(1, RegionMask.ScaleFor(0));
        Assert.Equal(1, RegionMask.ScaleFor(1));
        Assert.Equal(0.5, RegionMask.ScaleFor(2), 9);
        Assert.Equal(RegionMask.MinScale, RegionMask.ScaleFor(500));
    }

    [Fact]
    public void Build_FitLetterbox_RegionCoversOnlyImage()
    {
        var mask = Build(Square, new RectangleF(0, 25, 100, 50), 0, false, Clockwise(0, 0, 1, 1));

        Assert.Equal(0, At(mask, 50, 10));
        Assert.Equal(255, At(mask, 50, 50));
        Assert.Equal(0, At(mask, 50, 90));
    }

    [Fact]
    public void Build_CenterCrop_MapsThroughFullImageRect()
    {
        var mask = Build(new Size(1000, 1000), new RectangleF(-1500, -500, 4000, 2000), 0, false, Clockwise(0.375, 0, 0.5, 1));

        Assert.Equal(255, At(mask, 250, 500));
        Assert.Equal(255, At(mask, 499, 500));
        Assert.Equal(0, At(mask, 500, 500));
        Assert.Equal(0, At(mask, 750, 500));
    }

    [Fact]
    public void Build_Feather_IsTheSameWhereverTheRegionSits()
    {
        var target = new Size(200, 100);
        var left = Build(target, 1, false, Clockwise(0.2, 0.3, 0.4, 0.7));
        var right = Build(target, 1, false, Clockwise(0.6, 0.3, 0.8, 0.7));

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                Assert.Equal(At(left, x, y), At(right, x + 80, y));
            }
        }
    }

    [Fact]
    public void Build_Feather_FadesToZeroAwayFromRegion()
    {
        var mask = Build(new Size(200, 100), 1, false, Clockwise(0.4, 0.4, 0.6, 0.6));

        Assert.Equal(0, At(mask, 0, 0));
        Assert.Equal(0, At(mask, 199, 99));
        Assert.Equal(0, At(mask, 100, 5));
        Assert.Equal(255, At(mask, 100, 50));
    }

    [Fact]
    public void Build_InvertedFeather_KeepsFrameEdgesWhite()
    {
        var mask = Build(new Size(200, 100), 1, true, Clockwise(0.4, 0.4, 0.6, 0.6));

        Assert.Equal(255, At(mask, 0, 0));
        Assert.Equal(255, At(mask, 199, 99));
        Assert.Equal(0, At(mask, 100, 50));
    }
}
