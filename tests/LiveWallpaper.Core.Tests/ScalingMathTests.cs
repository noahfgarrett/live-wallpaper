using System.Drawing;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Tests;

public sealed class ScalingMathTests
{
    private static readonly Size Square = new(1000, 1000);
    private static readonly Size Hd = new(1920, 1080);

    [Theory]
    [InlineData(WallpaperScaling.Fill, 0, -420, 1920, 1920)]
    [InlineData(WallpaperScaling.Fit, 420, 0, 1080, 1080)]
    [InlineData(WallpaperScaling.Stretch, 0, 0, 1920, 1080)]
    [InlineData(WallpaperScaling.Center, 460, 40, 1000, 1000)]
    public void ComputeDestination_SquareImageOnHd(WallpaperScaling scaling, float x, float y, float w, float h)
    {
        Assert.Equal(new RectangleF(x, y, w, h), ScalingMath.ComputeDestination(Square, Hd, scaling));
    }

    [Fact]
    public void ComputeDestination_CenterCropsLargerImageSymmetrically()
    {
        Assert.Equal(new RectangleF(-1040, -960, 4000, 3000),
            ScalingMath.ComputeDestination(new Size(4000, 3000), Hd, WallpaperScaling.Center));
    }

    [Theory]
    [InlineData(4000, 3000, WallpaperScaling.Fill, 1920, 1440)]
    [InlineData(800, 600, WallpaperScaling.Fill, 800, 600)]
    [InlineData(4000, 3000, WallpaperScaling.Fit, 1440, 1080)]
    [InlineData(4000, 3000, WallpaperScaling.Stretch, 1920, 1080)]
    [InlineData(20000, 10000, WallpaperScaling.Center, 1920, 1080)]
    [InlineData(1000, 800, WallpaperScaling.Center, 1000, 800)]
    public void ComputeDecodeSize_NeverUpscalesAndRespectsTextureCap(int iw, int ih, WallpaperScaling scaling, int ew, int eh)
    {
        Assert.Equal(new Size(ew, eh), ScalingMath.ComputeDecodeSize(new Size(iw, ih), Hd, scaling));
    }

    [Fact]
    public void ComputeDecodeSize_CapsAtMaxTextureDimension()
    {
        Assert.Equal(new Size(16384, 8192),
            ScalingMath.ComputeDecodeSize(new Size(20000, 10000), new Size(40000, 20000), WallpaperScaling.Fit));
    }

    [Fact]
    public void ComputeSourceCrop_CenterLargerImage_ReturnsCenteredTargetSizedRegion()
    {
        Assert.Equal(new Rectangle(1040, 960, 1920, 1080),
            ScalingMath.ComputeSourceCrop(new Size(4000, 3000), Hd, WallpaperScaling.Center));
    }

    [Fact]
    public void ComputeSourceCrop_CenterSmallerImage_ReturnsWholeImage()
    {
        Assert.Equal(new Rectangle(0, 0, 1000, 1000), ScalingMath.ComputeSourceCrop(Square, Hd, WallpaperScaling.Center));
    }

    [Theory]
    [InlineData(WallpaperScaling.Fill)]
    [InlineData(WallpaperScaling.Fit)]
    [InlineData(WallpaperScaling.Stretch)]
    public void ComputeSourceCrop_OtherModes_ReturnWholeImage(WallpaperScaling scaling)
    {
        Assert.Equal(new Rectangle(0, 0, 4000, 3000), ScalingMath.ComputeSourceCrop(new Size(4000, 3000), Hd, scaling));
    }

    [Theory]
    [InlineData(0, 10, 10, 10)]
    [InlineData(10, -1, 10, 10)]
    [InlineData(10, 10, 0, 10)]
    public void ComputeDestination_NonPositiveSizesThrow(int iw, int ih, int tw, int th)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScalingMath.ComputeDestination(new Size(iw, ih), new Size(tw, th), WallpaperScaling.Fill));
    }

    [Fact]
    public void ComputeDecodeSize_NonPositiveSizesThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScalingMath.ComputeDecodeSize(new Size(0, 0), Hd, WallpaperScaling.Fill));
    }

    [Fact]
    public void ComputeScaledDestination_Center_ShowsWhatTheMonitorShows()
    {
        var bounds = ScalingMath.ComputeScaledDestination(new Size(4000, 2000), Hd, new Size(960, 540), WallpaperScaling.Center);

        Assert.Equal(new RectangleF(-520, -230, 2000, 1000), bounds);
    }

    [Theory]
    [InlineData(WallpaperScaling.Fill)]
    [InlineData(WallpaperScaling.Fit)]
    [InlineData(WallpaperScaling.Stretch)]
    public void ComputeScaledDestination_ScaleInvariantModes_MatchTheTarget(WallpaperScaling scaling)
    {
        var scaled = ScalingMath.ComputeScaledDestination(new Size(4000, 3000), Hd, new Size(960, 540), scaling);
        var direct = ScalingMath.ComputeDestination(new Size(4000, 3000), new Size(960, 540), scaling);

        Assert.Equal(direct.X, scaled.X, 3);
        Assert.Equal(direct.Y, scaled.Y, 3);
        Assert.Equal(direct.Width, scaled.Width, 3);
        Assert.Equal(direct.Height, scaled.Height, 3);
    }
}
