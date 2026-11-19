using System.Drawing;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Rendering;
using Vortice.Mathematics;
using Size = System.Drawing.Size;

namespace LiveWallpaper.Windows.Tests;

public sealed class RegionOverlayRendererTests
{
    private static readonly Size Target = new(100, 100);

    private static byte[] Draw(params OverlayShape[] shapes)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var overlay = new RegionOverlayRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        gpu.Context.ClearRenderTargetView(target.View, new Color4(0, 0, 0, 1));
        overlay.Draw(target.View, Target, new RectangleF(0, 0, Target.Width, Target.Height), shapes, 1);
        return target.ReadBack();
    }

    private static int Brightness(byte[] pixels, int x, int y)
    {
        var i = ((y * Target.Width) + x) * 4;
        return pixels[i] + pixels[i + 1] + pixels[i + 2];
    }

    [Fact]
    public void Shaders_Compile()
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(RegionOverlayRenderer.ShaderResource, "VSMain", "vs_5_0").IsEmpty);
        Assert.False(ShaderCompiler.Compile(RegionOverlayRenderer.ShaderResource, "PSMain", "ps_5_0").IsEmpty);
    }

    [Fact]
    public void Handle_DrawsAtItsPoint()
    {
        WindowsOnly.Require();
        var centre = new RegionPoint(0.5, 0.5);

        var pixels = Draw(new OverlayShape(OverlayShapeKind.Handle, centre, centre));

        Assert.True(Brightness(pixels, 50, 50) > 600);
        Assert.Equal(0, Brightness(pixels, 10, 10));
    }

    [Fact]
    public void Edge_DrawsAlongSegment()
    {
        WindowsOnly.Require();

        var pixels = Draw(new OverlayShape(OverlayShapeKind.Edge, new RegionPoint(0.1, 0.5), new RegionPoint(0.9, 0.5)));

        Assert.True(Brightness(pixels, 50, 50) > 500);
        Assert.Equal(0, Brightness(pixels, 50, 20));
    }

    [Fact]
    public void NoShapes_LeavesTargetUntouched()
    {
        WindowsOnly.Require();

        var pixels = Draw();

        Assert.All(Enumerable.Range(0, Target.Width * Target.Height), i => Assert.Equal(0, Brightness(pixels, i % Target.Width, i / Target.Width)));
    }
}
