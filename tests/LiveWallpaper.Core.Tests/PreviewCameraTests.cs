using System.Drawing;
using LiveWallpaper.Core.Geometry;

namespace LiveWallpaper.Core.Tests;

public sealed class PreviewCameraTests
{
    private static readonly Size Render = new(1920, 1080);
    private static readonly Size View = new(960, 540);

    [Fact]
    public void Fit_CentresAndScales()
    {
        var camera = PreviewCamera.Fit(Render, View);
        Assert.Equal(0.5, camera.Scale, 9);
        Assert.Equal(PointF.Empty, camera.Offset);
        Assert.True(camera.IsFit);

        var wide = PreviewCamera.Fit(Render, new Size(1000, 540));
        Assert.Equal(0.5, wide.Scale, 9);
        Assert.Equal(-40f, wide.Offset.X, 3);
        Assert.Equal(0f, wide.Offset.Y, 3);
    }

    [Theory]
    [InlineData(5120, 2880, 3840, 2160)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(2560, 1600, 2560, 1600)]
    [InlineData(0, 0, 1, 1)]
    public void RenderSizeFor_CapsAt4K(int w, int h, int ew, int eh)
    {
        Assert.Equal(new Size(ew, eh), PreviewCamera.RenderSizeFor(new Size(w, h)));
    }

    [Fact]
    public void CanZoomIn_IsFalseOnlyAtTheMaximum()
    {
        var fit = PreviewCamera.Fit(Render, View);
        Assert.True(fit.CanZoomIn);
        Assert.False(fit.ToActual().ZoomAt(new PointF(480, 270), 100).CanZoomIn);
    }

    [Fact]
    public void CanZoomOut_IsFalseOnlyAtFit()
    {
        var fit = PreviewCamera.Fit(Render, View);
        Assert.False(fit.CanZoomOut);
        Assert.True(fit.StepIn().CanZoomOut);
    }

    [Fact]
    public void ZoomAt_KeepsThePointUnderTheCursor()
    {
        var camera = PreviewCamera.Fit(Render, View);
        var cursor = new PointF(300, 200);
        var before = camera.ToRender(cursor);

        var zoomed = camera.ZoomAt(cursor, 2);

        var after = zoomed.ToRender(cursor);
        Assert.Equal(1.0, zoomed.Scale, 9);
        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
    }

    [Fact]
    public void ZoomAt_ClampsToFitAndTo400()
    {
        var camera = PreviewCamera.Fit(Render, View);
        Assert.Equal(4.0, camera.ToActual().ZoomAt(new PointF(10, 10), 100).Scale, 9);
        Assert.Equal(0.5, camera.ZoomAt(new PointF(10, 10), 0.1).Scale, 9);
    }

    [Fact]
    public void ZoomAt_CrossingActual_SnapsTo100()
    {
        var cursor = new PointF(333.3f, 201.7f);
        var camera = PreviewCamera.Fit(Render, View);
        var scales = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            camera = camera.ZoomAt(cursor, PreviewCamera.StepFactor);
            scales.Add(camera.Scale);
        }

        Assert.Equal(new[] { 0.625, 0.78125, 0.9765625, 1.0, 1.25 }, scales.ToArray());
        var down = camera.ZoomAt(cursor, 0.5);
        Assert.Equal(1.0, down.Scale);
        Assert.Equal(Math.Round(down.Offset.X), down.Offset.X);
        Assert.Equal(Math.Round(down.Offset.Y), down.Offset.Y);
    }

    [Fact]
    public void ZoomOutPastFit_IsCentred()
    {
        var zoomed = PreviewCamera.Fit(Render, new Size(1000, 540)).ZoomAt(new PointF(100, 100), 4);
        var camera = zoomed.ZoomAt(new PointF(100, 100), 0.01);

        Assert.True(camera.IsFit);
        Assert.Equal(-40f, camera.Offset.X, 3);
        Assert.Equal(0f, camera.Offset.Y, 3);
    }

    [Fact]
    public void Pan_AtFit_DoesNothing()
    {
        var camera = PreviewCamera.Fit(Render, View);
        Assert.Equal(camera, camera.Pan(new SizeF(50, -30)));
    }

    [Fact]
    public void Pan_StopsAtEdges()
    {
        var camera = PreviewCamera.Fit(Render, View).ToActual();
        Assert.Equal(480f, camera.Offset.X, 3);

        var dragRight = camera.Pan(new SizeF(100, 0));
        Assert.Equal(380f, dragRight.Offset.X, 3);

        var farRight = camera.Pan(new SizeF(100000, 100000));
        Assert.Equal(0f, farRight.Offset.X, 3);
        Assert.Equal(0f, farRight.Offset.Y, 3);

        var farLeft = camera.Pan(new SizeF(-100000, -100000));
        Assert.Equal(960f, farLeft.Offset.X, 3);
        Assert.Equal(540f, farLeft.Offset.Y, 3);
    }

    [Fact]
    public void StepIn_SnapsTo100_WhenCrossing()
    {
        var camera = PreviewCamera.Fit(Render, View);
        var scales = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            camera = camera.StepIn();
            scales.Add(camera.Scale);
        }

        Assert.Equal(new[] { 0.625, 0.78125, 0.9765625, 1.0, 1.25 }, scales.ToArray());
    }

    [Fact]
    public void StepOut_SnapsToFit()
    {
        var camera = PreviewCamera.Fit(Render, View).ToActual();
        Assert.Equal(1.0, camera.Scale, 9);

        camera = camera.StepOut();
        Assert.Equal(0.8, camera.Scale, 9);
        camera = camera.StepOut();
        Assert.Equal(0.64, camera.Scale, 9);
        camera = camera.StepOut();
        Assert.Equal(0.512, camera.Scale, 9);
        camera = camera.StepOut();
        Assert.Equal(0.5, camera.Scale, 9);
        Assert.True(camera.IsFit);

        var fromAbove = PreviewCamera.Fit(Render, View).ToActual().ZoomAt(PointF.Empty, 1.5);
        Assert.Equal(1.5, fromAbove.Scale, 9);
        Assert.Equal(1.2, fromAbove.StepOut().Scale, 9);
        Assert.Equal(1.0, fromAbove.StepOut().StepOut().Scale, 9);
    }

    [Fact]
    public void ToActual_KeepsCentre()
    {
        var camera = PreviewCamera.Fit(Render, View);
        var centre = camera.ToRender(new PointF(480, 270));

        var actual = camera.ToActual();

        var after = actual.ToRender(new PointF(480, 270));
        Assert.Equal(1.0, actual.Scale, 9);
        Assert.Equal(centre.X, after.X, 2);
        Assert.Equal(centre.Y, after.Y, 2);
    }

    [Theory]
    [InlineData(960, 540)]
    [InlineData(961, 541)]
    public void ToActual_HasWholePixelOffset(int viewWidth, int viewHeight)
    {
        var view = new Size(viewWidth, viewHeight);
        var camera = PreviewCamera.Fit(Render, view).ZoomAt(new PointF(333, 201), 1.7).ToActual();

        Assert.Equal(1.0, camera.Scale, 9);
        AssertWholePixelsInRange(camera);
    }

    [Fact]
    public void ZoomAtToActual_HasWholePixelOffset()
    {
        var camera = PreviewCamera.Fit(Render, View).ZoomAt(new PointF(333.3f, 201.7f), 2);

        Assert.Equal(1.0, camera.Scale, 9);
        AssertWholePixelsInRange(camera);
    }

    [Fact]
    public void Resize_AtFit_StaysFit()
    {
        var camera = PreviewCamera.Fit(Render, View).Resize(new Size(640, 480));
        Assert.True(camera.IsFit);
        Assert.Equal(new Size(640, 480), camera.View);
        Assert.Equal(640.0 / 1920, camera.Scale, 9);
    }

    [Fact]
    public void Resize_Zoomed_KeepsCentre()
    {
        var camera = PreviewCamera.Fit(Render, View).ToActual().Pan(new SizeF(-100, -50));
        var centre = camera.ToRender(new PointF(480, 270));

        var resized = camera.Resize(new Size(800, 400));

        var after = resized.ToRender(new PointF(400, 200));
        Assert.Equal(1.0, resized.Scale, 9);
        Assert.Equal(centre.X, after.X, 2);
        Assert.Equal(centre.Y, after.Y, 2);
    }

    [Fact]
    public void MapRect_RoundTrips()
    {
        var camera = PreviewCamera.Fit(Render, View).ZoomAt(new PointF(200, 100), 2);
        var rect = new RectangleF(400, 300, 200, 100);

        var mapped = camera.MapRect(rect);
        var topLeft = camera.ToRender(new PointF(mapped.X, mapped.Y));

        Assert.Equal(rect.X, topLeft.X, 2);
        Assert.Equal(rect.Y, topLeft.Y, 2);
        Assert.Equal(rect.Width * camera.Scale, mapped.Width, 2);
        var back = camera.ToView(topLeft);
        Assert.Equal(mapped.X, back.X, 2);
        Assert.Equal(mapped.Y, back.Y, 2);
    }

    [Fact]
    public void ZoomPercent_IsRelativeToRenderPixels()
    {
        var camera = PreviewCamera.Fit(new Size(3840, 2160), new Size(1920, 1080));
        Assert.Equal(50, camera.ZoomPercent);
        Assert.Equal(100, camera.ToActual().ZoomPercent);
    }

    private static void AssertWholePixelsInRange(PreviewCamera camera)
    {
        Assert.Equal(Math.Round(camera.Offset.X), camera.Offset.X);
        Assert.Equal(Math.Round(camera.Offset.Y), camera.Offset.Y);
        Assert.InRange(camera.Offset.X, 0, camera.Render.Width - camera.View.Width);
        Assert.InRange(camera.Offset.Y, 0, camera.Render.Height - camera.View.Height);
    }
}
