using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class SnowEffectTests
{
    private static readonly Size Target = new(256, 144);

    private static byte[] Render(SnowSettings? snow, uint seed = 7, int frames = 30)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(background, gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10)), new RectangleF(0, 0, Target.Width, Target.Height));
        if (snow is not null)
        {
            var effect = new SnowEffect(gpu, seed);
            effect.Apply(snow);
            scene.Effects.Add(effect);
        }
        for (var i = 1; i <= frames; i++)
        {
            scene.Update(new FrameTime(1.0 / 60, i / 60.0), Target);
        }
        scene.Render(target.View, Target);
        return target.ReadBack();
    }

    private static double BrighterFraction(byte[] pixels, byte[] background)
    {
        var brighter = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var delta = pixels[i] + pixels[i + 1] + pixels[i + 2] - background[i] - background[i + 1] - background[i + 2];
            if (delta > 30)
            {
                brighter++;
            }
        }
        return brighter / (double)(pixels.Length / 4);
    }

    [Fact]
    public void Snow_ShadersCompile()
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(SnowEffect.ShaderResource, "VSMain", "vs_5_0").IsEmpty);
        Assert.False(ShaderCompiler.Compile(SnowEffect.ShaderResource, "PSMain", "ps_5_0").IsEmpty);
    }

    [Fact]
    public void Snow_Disabled_LeavesBackgroundUntouched()
    {
        WindowsOnly.Require();

        Assert.Equal(Render(null), Render(new SnowSettings(Enabled: false, DensityPct: 100)));
    }

    [Fact]
    public void Snow_Density100_LightensSomePixels()
    {
        WindowsOnly.Require();

        var fraction = BrighterFraction(Render(new SnowSettings(DensityPct: 100, SizePct: 100)), Render(null));

        Assert.InRange(fraction, 0.01, 0.60);
    }

    [Fact]
    public void Snow_MotionBlur_StillDraws()
    {
        WindowsOnly.Require();

        Assert.True(BrighterFraction(Render(new SnowSettings(DensityPct: 100, SizePct: 100, MotionBlur: true)), Render(null)) > 0.005);
    }

    [Fact]
    public void Snow_SameSeedAndTime_RendersIdentically()
    {
        WindowsOnly.Require();
        var settings = new SnowSettings(DensityPct: 80);

        Assert.Equal(Render(settings), Render(settings));
    }

    [Fact]
    public void Scene_IsAnimatedOnlyWithSnowEnabled()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var scene = new WallpaperScene(background, gpu.CreateImageTexture(OffscreenTarget.Solid(0, 0, 0)), new RectangleF(0, 0, 10, 10));
        var effect = new SnowEffect(gpu, 1);
        scene.Effects.Add(effect);

        effect.Apply(new SnowSettings(Enabled: false));
        Assert.False(scene.IsAnimated);
        effect.Apply(new SnowSettings(DensityPct: 0));
        Assert.False(scene.IsAnimated);
        effect.Apply(new SnowSettings(DensityPct: 40));
        Assert.True(scene.IsAnimated);
    }

    [Fact]
    public void Scene_ReportsActiveParticles()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var scene = new WallpaperScene(background, gpu.CreateImageTexture(OffscreenTarget.Solid(0, 0, 0)), new RectangleF(0, 0, 1920, 1080));
        var effect = new SnowEffect(gpu, 1);
        effect.Apply(new SnowSettings(DensityPct: 50));
        scene.Effects.Add(effect);

        scene.Update(new FrameTime(1.0 / 60, 1), new Size(1920, 1080));

        Assert.Equal(1100, scene.ActiveParticles);
    }
}
