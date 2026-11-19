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
    [InlineData(20000, 10000, WallpaperScaling.Center, 16384, 8192)]
    public void ComputeDecodeSize_NeverUpscalesAndRespectsTextureCap(int iw, int ih, WallpaperScaling scaling, int ew, int eh)
    {
        Assert.Equal(new Size(ew, eh), ScalingMath.ComputeDecodeSize(new Size(iw, ih), Hd, scaling));
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
}
