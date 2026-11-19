using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Water;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class WaterEffectTests
{
    private const double FrameSeconds = 1.0 / 60;
    private static readonly Size Target = new(960, 540);
    private static readonly RectangleF FullFrame = new(0, 0, Target.Width, Target.Height);
    private static readonly WaterSettings Flowing = new(Enabled: true, StretchPct: 50, FoamPct: 30, MistPct: 0);

    private const double RegionLeft = 1.0 / 3;
    private const double RegionRight = 2.0 / 3;
    private const double RegionTop = 0.1;
    private const double RegionBottom = 0.6;

    private static RegionSet MiddleThirdFlowingDown() =>
        new([new Region([new(RegionLeft, RegionTop), new(RegionRight, RegionTop), new(RegionRight, RegionBottom), new(RegionLeft, RegionBottom)])
        {
            Flow = new RegionPoint(0, 0.4),
        }], Invert: false, FeatherPct: 0);

    /// <summary>Horizontal stripes of two dark colours, so a downward flow moves visible edges.</summary>
    private static DecodedImage Stripes()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var isFirst = (y / 4) % 2 == 0;
            for (var x = 0; x < size; x++)
            {
                var i = ((y * size) + x) * 4;
                pixels[i] = isFirst ? (byte)30 : (byte)90;
                pixels[i + 1] = isFirst ? (byte)60 : (byte)30;
                pixels[i + 2] = isFirst ? (byte)40 : (byte)20;
                pixels[i + 3] = 255;
            }
        }
        return new DecodedImage(size, size, size * 4, pixels);
    }

    private static byte[] Render(WaterSettings? water, double seconds = 0.5, RegionSet? regions = null)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(Stripes()), FullFrame, FullFrame);
        if (water is not null)
        {
            var effect = new WaterEffect(gpu);
            effect.Apply(water);
            scene.Effects.Add(effect);
        }
        if (regions is not null)
        {
            scene.SetRegions(EffectKind.Water, regions);
        }
        var frames = (int)Math.Round(seconds / FrameSeconds);
        for (var i = 1; i <= frames; i++)
        {
            scene.Update(new FrameTime(FrameSeconds, i * FrameSeconds), Target);
        }
        scene.Render(target.View, Target);
        return target.ReadBack();
    }

    private static bool IsInsideRegion(int x, int y, int margin) =>
        x >= (RegionLeft * Target.Width) - margin && x < (RegionRight * Target.Width) + margin
        && y >= (RegionTop * Target.Height) - margin && y < (RegionBottom * Target.Height) + margin;

    private static int Differing(byte[] a, byte[] b, Func<int, int, bool> include)
    {
        var count = 0;
        for (var y = 0; y < Target.Height; y++)
        {
            for (var x = 0; x < Target.Width; x++)
            {
                if (!include(x, y))
                {
                    continue;
                }
                var i = ((y * Target.Width) + x) * 4;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static int BelowRegionBrightened(byte[] pixels, byte[] background)
    {
        var count = 0;
        var top = (int)Math.Ceiling(RegionBottom * Target.Height) + 2;
        for (var y = top; y < top + 30; y++)
        {
            for (var x = (int)Math.Ceiling(RegionLeft * Target.Width); x < (int)(RegionRight * Target.Width); x++)
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
    public void Water_OutsideRegion_EqualsBackground()
    {
        WindowsOnly.Require();
        var background = Render(null);

        var pixels = Render(Flowing, regions: MiddleThirdFlowingDown());

        Assert.Equal(0, Differing(pixels, background, (x, y) => !IsInsideRegion(x, y, margin: 2)));
    }

    [Fact]
    public void Water_InsideRegion_ChangesOverTime()
    {
        WindowsOnly.Require();

        var early = Render(Flowing, seconds: 0.5, regions: MiddleThirdFlowingDown());
        var late = Render(Flowing, seconds: 1.3, regions: MiddleThirdFlowingDown());

        var changed = Differing(early, late, (x, y) => IsInsideRegion(x, y, margin: -2));
        Assert.True(changed >= 1000, $"only {changed} pixels changed");
    }

    [Fact]
    public void Water_NoRegions_DrawsNothing()
    {
        WindowsOnly.Require();

        Assert.Equal(Render(null), Render(Flowing with { MistPct = 100 }));
    }

    [Fact]
    public void Water_IsAnimated_OnlyWhenEnabledWithRegions()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var effect = new WaterEffect(gpu);
        effect.Apply(Flowing);

        effect.SetGeometry(RegionSet.Empty, FullFrame, Target);
        var withoutRegions = effect.IsAnimated;
        effect.SetGeometry(MiddleThirdFlowingDown(), FullFrame, Target);
        var withRegions = effect.IsAnimated;
        effect.Apply(Flowing with { Enabled = false });
        var disabled = effect.IsAnimated;

        Assert.Equal((false, true, false), (withoutRegions, withRegions, disabled));
    }

    [Fact]
    public void Water_Mist_BrightensBelowTheRegion_OnlyWhenMistOn()
    {
        WindowsOnly.Require();
        var background = Render(null);

        var misty = Render(Flowing with { MistPct = 100 }, regions: MiddleThirdFlowingDown());
        var clear = Render(Flowing with { MistPct = 0 }, regions: MiddleThirdFlowingDown());

        var top = (int)Math.Ceiling(RegionBottom * Target.Height) + 2;
        Assert.True(BelowRegionBrightened(misty, background) >= 1000, $"only {BelowRegionBrightened(misty, background)} pixels brightened");
        Assert.Equal(0, Differing(clear, background, (_, y) => y >= top && y < top + 30));
    }

    [Fact]
    public void FlowTexture_Update_UsesImageSize()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var flow = new FlowTexture(gpu);

        flow.Update(new FlowImage(3, 2, new float[3 * 2 * 2]));

        Assert.Equal(new Size(3, 2), flow.PixelSize);
    }
}
