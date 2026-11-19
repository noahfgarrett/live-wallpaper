using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class GlassEffectTests
{
    private const double FrameSeconds = 1.0 / 60;
    private static readonly Size Target = new(960, 540);
    private static readonly RectangleF FullFrame = new(0, 0, Target.Width, Target.Height);
    private static readonly GlassSettings Fogged = new(Enabled: true, DropsPct: 50, DropSizePct: 50, SlidingPct: 40, FogPct: 100);

    private static Region Quad(params (double X, double Y)[] points) =>
        new(points.Select(p => new RegionPoint(p.X, p.Y)).ToArray());

    private static RegionSet Panes(double featherPct, params Region[] panes) => new(panes, Invert: false, FeatherPct: featherPct);

    private static RegionSet LeftHalf() => Panes(0, Quad((0, 0), (0.5, 0), (0.5, 1), (0, 1)));

    /// <summary>A pane over the top left, whose bounding box the perspective pane below reaches into without touching it.</summary>
    private static Region TopLeft() => Quad((0.05, 0.1), (0.45, 0.1), (0.45, 0.4), (0.05, 0.4));

    private static Region Perspective() => Quad((0.55, 0.1), (0.95, 0.1), (0.95, 0.9), (0.3, 0.9));

    /// <summary>A checker of dark and light squares, never black.</summary>
    private static DecodedImage Checker()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var isLight = ((x / 8) + (y / 8)) % 2 == 0;
                var i = ((y * size) + x) * 4;
                pixels[i] = isLight ? (byte)230 : (byte)20;
                pixels[i + 1] = isLight ? (byte)220 : (byte)25;
                pixels[i + 2] = isLight ? (byte)210 : (byte)30;
                pixels[i + 3] = 255;
            }
        }
        return new DecodedImage(size, size, size * 4, pixels);
    }

    private static byte[] Render(GlassSettings? glass, RegionSet? regions = null, double seconds = 3)
    {
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(Checker()), FullFrame, FullFrame);
        if (glass is not null)
        {
            var effect = new GlassEffect(gpu);
            effect.Apply(glass);
            scene.Effects.Add(effect);
        }
        if (regions is not null)
        {
            scene.SetRegions(EffectKind.Glass, regions);
        }
        var frames = (int)Math.Round(seconds / FrameSeconds);
        for (var i = 1; i <= frames; i++)
        {
            scene.Update(new FrameTime(FrameSeconds, i * FrameSeconds), Target);
        }
        scene.Render(target.View, Target);
        return target.ReadBack();
    }

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

    private static int Black(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsInside(int x, int y, double left, double top, double right, double bottom, int inset) =>
        x >= (left * Target.Width) + inset && x < (right * Target.Width) - inset
        && y >= (top * Target.Height) + inset && y < (bottom * Target.Height) - inset;

    [Fact]
    public void Glass_OutsidePane_EqualsFrame()
    {
        WindowsOnly.Require();
        var frame = Render(null);

        var pixels = Render(Fogged, LeftHalf());

        Assert.Equal(0, Differing(pixels, frame, (x, _) => x >= Target.Width / 2));
    }

    [Fact]
    public void Glass_InsidePane_ChangesPixels()
    {
        WindowsOnly.Require();
        var frame = Render(null);

        var pixels = Render(Fogged, LeftHalf());

        var changed = Differing(pixels, frame, (x, _) => x < Target.Width / 2);
        Assert.True(changed > Target.Width / 2 * Target.Height / 2, $"only {changed} pixels changed");
    }

    [Fact]
    public void Glass_NoPanes_CoversEverything()
    {
        WindowsOnly.Require();
        var frame = Render(null);

        var pixels = Render(Fogged, RegionSet.Empty);

        var quarter = Target.Width / 2 * Target.Height / 2;
        var corners = new[]
        {
            Differing(pixels, frame, (x, y) => x < Target.Width / 2 && y < Target.Height / 2),
            Differing(pixels, frame, (x, y) => x >= Target.Width / 2 && y < Target.Height / 2),
            Differing(pixels, frame, (x, y) => x < Target.Width / 2 && y >= Target.Height / 2),
            Differing(pixels, frame, (x, y) => x >= Target.Width / 2 && y >= Target.Height / 2),
        };
        Assert.All(corners, changed => Assert.True(changed > quarter / 2, $"only {changed} of {quarter} pixels changed"));
    }

    [Fact]
    public void Glass_DegeneratePane_IsSkipped_NoNaN()
    {
        WindowsOnly.Require();
        var collinear = Quad((0.1, 0.1), (0.3, 0.3), (0.5, 0.5), (0.2, 0.2));

        var pixels = Render(Fogged, Panes(5, collinear, Perspective()));

        Assert.Equal(0, Black(pixels));
    }

    [Fact]
    public void Glass_AllPanesDegenerate_DrawsNothing()
    {
        WindowsOnly.Require();
        var collinear = Quad((0.1, 0.1), (0.3, 0.3), (0.5, 0.5), (0.2, 0.2));

        Assert.Equal(Render(null), Render(Fogged, Panes(0, collinear)));
    }

    [Fact]
    public void Glass_OverlappingBounds_EachPaneKeepsItsOwnPixels()
    {
        WindowsOnly.Require();
        var alone = Render(Fogged, Panes(0, TopLeft()));

        var withNeighbour = Render(Fogged, Panes(0, TopLeft(), Perspective()));

        Assert.Equal(0, Differing(alone, withNeighbour, (x, y) => IsInside(x, y, 0.05, 0.1, 0.45, 0.4, inset: 2)));
    }

    [Fact]
    public void Glass_NeedsFrameCopy_OnlyWhenEnabled()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var effect = new GlassEffect(gpu);

        effect.Apply(Fogged);
        var enabled = (effect.NeedsFrameCopy, effect.IsAnimated, effect.ActiveParticles);
        effect.Apply(Fogged with { Enabled = false });
        var disabled = (effect.NeedsFrameCopy, effect.IsAnimated, effect.ActiveParticles);

        Assert.Equal(((true, true, 0), (false, false, 0)), (enabled, disabled));
    }
}
