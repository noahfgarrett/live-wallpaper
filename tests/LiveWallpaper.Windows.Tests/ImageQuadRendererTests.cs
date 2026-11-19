using System.Drawing;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Windows.Tests;

public sealed unsafe class ImageQuadRendererTests
{
    private static readonly Size Target = new(64, 32);

    private static DecodedImage SolidRed(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        return new DecodedImage(width, height, width * 4, pixels);
    }

    private static byte[] RenderAndReadBack(RectangleF destination, params Point[] samples)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var renderer = new ImageQuadRenderer(gpu);
        using var image = gpu.CreateImageTexture(SolidRed(2, 2));

        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)Target.Width, (uint)Target.Height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        using var targetTexture = gpu.Device.CreateTexture2D(desc);
        using var rtv = gpu.Device.CreateRenderTargetView(targetTexture);

        renderer.Draw(rtv, Target, image, destination);

        var stagingDesc = desc with { BindFlags = BindFlags.None, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read };
        using var staging = gpu.Device.CreateTexture2D(stagingDesc);
        gpu.Context.CopyResource(staging, targetTexture);
        var mapped = gpu.Context.Map(staging, 0, MapMode.Read);
        try
        {
            var result = new List<byte>();
            foreach (var p in samples)
            {
                var row = (byte*)mapped.DataPointer + (p.Y * mapped.RowPitch);
                result.AddRange(new ReadOnlySpan<byte>(row + (p.X * 4), 4).ToArray());
            }
            return [.. result];
        }
        finally
        {
            gpu.Context.Unmap(staging, 0);
        }
    }

    [Fact]
    public void Draw_StretchRedImage_FillsTarget()
    {
        WindowsOnly.Require();

        var pixels = RenderAndReadBack(new RectangleF(0, 0, 64, 32), new Point(32, 16), new Point(0, 0), new Point(63, 31));

        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 }, pixels);
    }

    [Fact]
    public void Draw_FitImage_LeavesBlackBars()
    {
        WindowsOnly.Require();

        var pixels = RenderAndReadBack(new RectangleF(16, 0, 32, 32), new Point(4, 16), new Point(32, 16), new Point(60, 16));

        Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 255, 255, 0, 0, 0, 255 }, pixels);
    }
}
