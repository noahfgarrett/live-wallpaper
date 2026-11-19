using System.Drawing;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class RegionViewTests
{
    private static readonly Size Viewport = new(1000, 1000);

    private static RegionView View(Size image, WallpaperScaling scaling) =>
        new(ScalingMath.ComputeDestination(image, Viewport, scaling), Viewport, 1);

    [Fact]
    public void ToScreen_FitLetterbox_MapsCorners()
    {
        var view = View(new Size(1000, 500), WallpaperScaling.Fit);

        Assert.Equal(new RegionPoint(0, 250), view.ToScreen(new RegionPoint(0, 0)));
        Assert.Equal(new RegionPoint(1000, 750), view.ToScreen(new RegionPoint(1, 1)));
    }

    [Fact]
    public void ToImage_IsInverseOfToScreen()
    {
        var view = View(new Size(1600, 900), WallpaperScaling.Fill);
        var image = view.ToImage(view.ToScreen(new RegionPoint(0.3, 0.7)));

        Assert.Equal(0.3, image.X, 9);
        Assert.Equal(0.7, image.Y, 9);
    }

    [Fact]
    public void VisibleBounds_CenterCropOfLargeImage() =>
        Assert.Equal(new NormalizedRect(0.375, 0.25, 0.625, 0.75), View(new Size(4000, 2000), WallpaperScaling.Center).VisibleBounds);

    [Fact]
    public void VisibleBounds_FitShowsWholeImage() =>
        Assert.Equal(NormalizedRect.Unit, View(new Size(1000, 500), WallpaperScaling.Fit).VisibleBounds);

    [Fact]
    public void ClampToVisible_PointOutsideViewport_LandsOnVisibleEdge()
    {
        var view = View(new Size(1000, 500), WallpaperScaling.Fit);

        Assert.Equal(new RegionPoint(1000, 250), view.ClampToVisible(new RegionPoint(1400, -30)));
    }
}
