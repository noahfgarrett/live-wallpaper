using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class RainEffectTests
{
    private const double FrameSeconds = 1.0 / 60;
    private static readonly Size Target = new(1920, 1080);
    private static readonly RectangleF FullFrame = new(0, 0, Target.Width, Target.Height);
    private static readonly RainSettings HeavyRain = new(Enabled: true, DensityPct: 100, BrightnessPct: 100);

    private static RegionSet LeftHalf() =>
        new([new Region([new(0, 0), new(0.5, 0), new(0.5, 1), new(0, 1)])], Invert: false, FeatherPct: 0);

    private static byte[] Render(RainSettings? rain, int frames = 60, RegionSet? regions = null)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10)), FullFrame, FullFrame);
        if (rain is not null)
        {
            var effect = new RainEffect(gpu, 7);
            effect.Apply(rain);
            scene.Effects.Add(effect);
        }
        if (regions is not null)
        {
            scene.SetRegions(EffectKind.Rain, regions);
        }
        for (var i = 1; i <= frames; i++)
        {
            scene.Update(new FrameTime(FrameSeconds, i * FrameSeconds), Target);
        }
        scene.Render(target.View, Target);
        return target.ReadBack();
    }

    private static int Changed(byte[] pixels, byte[] background, int fromX, int toX)
    {
        var count = 0;
        for (var y = 0; y < Target.Height; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                var i = ((y * Target.Width) + x) * 4;
                if (pixels[i] != background[i] || pixels[i + 1] != background[i + 1] || pixels[i + 2] != background[i + 2])
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void Rain_BrightensOnlyInsideMask()
    {
        WindowsOnly.Require();
        var background = Render(null);

        var pixels = Render(HeavyRain, regions: LeftHalf());

        Assert.True(Changed(pixels, background, 0, 958) >= 1000, $"only {Changed(pixels, background, 0, 958)} pixels changed");
        Assert.Equal(0, Changed(pixels, background, 962, Target.Width));
    }

    [Fact]
    public void Rain_Disabled_LeavesBackground()
    {
        WindowsOnly.Require();

        Assert.Equal(Render(null), Render(HeavyRain with { Enabled = false }));
    }

    [Fact]
    public void Rain_FramesDiffer()
    {
        WindowsOnly.Require();

        Assert.NotEqual(Render(HeavyRain, frames: 60), Render(HeavyRain, frames: 66));
    }
}
