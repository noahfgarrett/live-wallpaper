using System.Drawing;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class PreviewPresenterTests
{
    private const byte BackgroundB = 33;
    private const byte BackgroundG = 20;
    private const byte BackgroundR = 13;

    [Fact]
    public void Present_At100Percent_CopiesPixelsExactly()
    {
        WindowsOnly.Require();
        var render = new Size(512, 512);
        var view = new Size(256, 256);
        var source = Gradient(render);
        var camera = new PreviewCamera(render, view, 1.0, new PointF(100, 50));

        var pixels = PresentAndReadBack(render, view, source, camera);

        for (var y = 0; y < view.Height; y++)
        {
            for (var x = 0; x < view.Width; x++)
            {
                var actual = (y * view.Width + x) * 4;
                var expected = ((y + 50) * render.Width + x + 100) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    Assert.InRange(pixels[actual + channel], source[expected + channel] - 1, source[expected + channel] + 1);
                }
            }
        }
    }

    [Fact]
    public void Present_AtHalfPixelOffset_AveragesNeighbouringTexels()
    {
        WindowsOnly.Require();
        var render = new Size(512, 512);
        var view = new Size(128, 64);
        var source = HorizontalRamp(render);
        var camera = new PreviewCamera(render, view, 1.0, new PointF(100.5f, 50));

        var pixels = PresentAndReadBack(render, view, source, camera);

        for (var y = 0; y < view.Height; y++)
        {
            for (var x = 0; x < view.Width; x++)
            {
                var left = ((y + 50) * render.Width + x + 100) * 4;
                var average = (source[left] + source[left + 4]) / 2.0;
                Assert.InRange(pixels[(y * view.Width + x) * 4], average - 1, average + 1);
            }
        }
    }

    [Fact]
    public void Present_AtFit_CheckerboardBecomesGrey()
    {
        WindowsOnly.Require();
        var render = new Size(2048, 2048);
        var view = new Size(256, 256);

        var pixels = PresentAndReadBack(render, view, Checkerboard(render), PreviewCamera.Fit(render, view));

        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.InRange(pixels[i + channel], 110, 145);
            }
        }
    }

    [Fact]
    public void Present_AtFit_0_7_CheckerboardBecomesGrey()
    {
        WindowsOnly.Require();

        AssertCheckerboardIsGreyAtFit(new Size(1434, 1434));
    }

    [Fact]
    public void Present_AtFit_0_35_CheckerboardBecomesGrey()
    {
        WindowsOnly.Require();

        AssertCheckerboardIsGreyAtFit(new Size(717, 717));
    }

    [Fact]
    public void Present_OutsideRender_IsBackground()
    {
        WindowsOnly.Require();
        var render = new Size(512, 512);
        var view = new Size(256, 128);
        var camera = PreviewCamera.Fit(render, view);

        var pixels = PresentAndReadBack(render, view, Solid(render, 255), camera);

        var image = camera.MapRect(new RectangleF(0, 0, render.Width, render.Height));
        Assert.Equal(new RectangleF(64, 0, 128, 128), image);
        foreach (var x in new[] { 0, 32, 63, 192, 220, 255 })
        {
            foreach (var y in new[] { 0, 64, 127 })
            {
                var i = (y * view.Width + x) * 4;
                Assert.InRange(pixels[i], BackgroundB - 1, BackgroundB + 1);
                Assert.InRange(pixels[i + 1], BackgroundG - 1, BackgroundG + 1);
                Assert.InRange(pixels[i + 2], BackgroundR - 1, BackgroundR + 1);
            }
        }
        var centre = (64 * view.Width + 128) * 4;
        Assert.Equal(new byte[] { 255, 255, 255 }, pixels[centre..(centre + 3)]);
    }

    [Fact]
    public void Begin_SameSize_ReturnsSameTarget()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);

        var first = presenter.Begin(new Size(64, 32));
        var second = presenter.Begin(new Size(64, 32));

        Assert.Same(first, second);
    }

    [Fact]
    public void Begin_NewSize_ReturnsNewTarget()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);

        var first = presenter.Begin(new Size(64, 32));
        var second = presenter.Begin(new Size(32, 64));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Present_CameraForOtherRenderSize_Throws()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);
        using var target = new OffscreenTarget(gpu, new Size(16, 16));
        presenter.Begin(new Size(64, 64));

        Assert.Throws<ArgumentException>(() =>
            presenter.Present(target.View, target.Size, PreviewCamera.Fit(new Size(32, 32), target.Size)));
    }

    [Fact]
    public void Present_BeforeBegin_Throws()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);
        using var target = new OffscreenTarget(gpu, new Size(16, 16));

        Assert.Throws<InvalidOperationException>(() =>
            presenter.Present(target.View, target.Size, PreviewCamera.Fit(new Size(16, 16), target.Size)));
    }

    [Fact]
    public void Present_IntoItsOwnTarget_Throws()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);
        var render = new Size(64, 64);
        var offscreen = presenter.Begin(render);

        Assert.Throws<InvalidOperationException>(() =>
            presenter.Present(offscreen, render, PreviewCamera.Fit(render, render)));
    }

    /// <summary>A 1-px checker in a 2048 x 2048 render, shown at Fit in <paramref name="view"/>, must average to grey with no beat pattern.</summary>
    private static void AssertCheckerboardIsGreyAtFit(Size view)
    {
        var render = new Size(2048, 2048);

        var pixels = PresentAndReadBack(render, view, Checkerboard(render), PreviewCamera.Fit(render, view));

        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.InRange(pixels[i + channel], 100, 155);
            }
        }
    }

    private static byte[] PresentAndReadBack(Size render, Size view, byte[] source, PreviewCamera camera)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var presenter = new PreviewPresenter(gpu);
        using var target = new OffscreenTarget(gpu, view);

        var offscreen = presenter.Begin(render);
        using (var resource = offscreen.Resource)
        {
            gpu.Context.UpdateSubresource(source, resource, 0, (uint)(render.Width * 4), 0, null);
        }
        presenter.Present(target.View, view, camera);

        return target.ReadBack();
    }

    private static byte[] Gradient(Size size)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * size.Width + x) * 4;
                pixels[i] = (byte)(x & 255);
                pixels[i + 1] = (byte)(y & 255);
                pixels[i + 2] = (byte)(((x >> 8) * 128) + ((y >> 8) * 64));
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>Blue rises by one per column from 90 at x = 100, staying linear across the columns the half-pixel test reads.</summary>
    private static byte[] HorizontalRamp(Size size)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * size.Width + x) * 4;
                pixels[i] = (byte)Math.Clamp(x - 10, 0, 255);
                pixels[i + 1] = 128;
                pixels[i + 2] = 64;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte[] Checkerboard(Size size)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * size.Width + x) * 4;
                var value = ((x + y) & 1) == 0 ? (byte)0 : (byte)255;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte[] Solid(Size size, byte value)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }
}
