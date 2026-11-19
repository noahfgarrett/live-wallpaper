using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class MaskedSnowTests
{
    private const int Frames = 30;
    private static readonly Size Target = new(1920, 1080);
    private static readonly RectangleF FullFrame = new(0, 0, Target.Width, Target.Height);
    private static readonly SnowSettings HeavySnow = new(DensityPct: 100, SizePct: 100);

    private static RegionSet Half(bool right, bool invert = false) =>
        new([right ? Band(0.5, 1) : Band(0, 0.5)], invert, FeatherPct: 0);

    private static Region Band(double left, double right) => new([new(left, 0), new(right, 0), new(right, 1), new(left, 1)]);

    private static byte[] Render(SnowSettings? snow, Action<WallpaperScene>? configure = null)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10)), FullFrame, FullFrame);
        if (snow is not null)
        {
            var effect = new SnowEffect(gpu, 7);
            effect.Apply(snow);
            scene.Effects.Add(effect);
        }
        configure?.Invoke(scene);
        for (var i = 1; i <= Frames; i++)
        {
            scene.Update(new FrameTime(1.0 / 60, i / 60.0), Target);
        }
        scene.Render(target.View, Target);
        return target.ReadBack();
    }

    private static int Brightened(byte[] pixels, byte[] background, int fromX, int toX)
    {
        var count = 0;
        for (var y = 0; y < Target.Height; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                var i = ((y * Target.Width) + x) * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] - background[i] - background[i + 1] - background[i + 2] > 30)
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void MaskTexture_NoRegions_IsOnePixel()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var mask = new RegionMaskTexture(gpu);

        Assert.Equal(new Size(1, 1), mask.PixelSize);
    }

    [Fact]
    public void MaskTexture_Update_UsesMaskSize()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var mask = new RegionMaskTexture(gpu);

        mask.Update(new MaskImage(3, 2, [0, 64, 128, 192, 255, 255]));
        Assert.Equal(new Size(3, 2), mask.PixelSize);
        mask.Update(null);
        Assert.Equal(new Size(1, 1), mask.PixelSize);
    }

    [Fact]
    public void Snow_RegionOnRightHalf_OnlySnowsThere()
    {
        WindowsOnly.Require();
        var background = Render(null);

        var pixels = Render(HeavySnow, scene => scene.SetRegions(EffectKind.Snow, Half(right: true)));

        Assert.Equal(0, Brightened(pixels, background, 0, 958));
        Assert.True(Brightened(pixels, background, 962, Target.Width) >= 1000);
    }

    [Fact]
    public void Snow_InvertedRegion_KeepsSnowOutOfIt()
    {
        WindowsOnly.Require();
        var background = Render(null);

        var pixels = Render(HeavySnow, scene => scene.SetRegions(EffectKind.Snow, Half(right: true, invert: true)));

        Assert.True(Brightened(pixels, background, 0, 958) >= 1000);
        Assert.Equal(0, Brightened(pixels, background, 962, Target.Width));
    }

    [Fact]
    public void Snow_RegionsChanged_MaskRebuilds()
    {
        WindowsOnly.Require();
        var background = Render(null);
        using var gpu = GpuDevice.Create(useWarp: true);
        using var quad = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(gpu, quad, gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10)), FullFrame, FullFrame);
        var effect = new SnowEffect(gpu, 7);
        effect.Apply(HeavySnow);
        scene.Effects.Add(effect);
        scene.SetRegions(EffectKind.Snow, Half(right: true));
        for (var i = 1; i <= Frames; i++)
        {
            scene.Update(new FrameTime(1.0 / 60, i / 60.0), Target);
        }
        scene.Render(target.View, Target);

        scene.SetRegions(EffectKind.Snow, Half(right: false));
        scene.Update(new FrameTime(1.0 / 60, (Frames + 1) / 60.0), Target);
        scene.Render(target.View, Target);
        var pixels = target.ReadBack();

        Assert.True(Brightened(pixels, background, 0, 958) >= 1000);
        Assert.Equal(0, Brightened(pixels, background, 962, Target.Width));
    }

    [Fact]
    public void Snow_EmptyRegions_MatchesUnmasked()
    {
        WindowsOnly.Require();

        Assert.Equal(Render(HeavySnow), Render(HeavySnow, scene => scene.SetRegions(EffectKind.Snow, RegionSet.Empty)));
    }
}
