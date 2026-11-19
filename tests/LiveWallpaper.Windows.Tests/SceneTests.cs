using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Rendering;
using Vortice.Direct3D11;

namespace LiveWallpaper.Windows.Tests;

public sealed class SceneTests
{
    private const int Frames = 30;
    private static readonly Size Target = new(1920, 1080);
    private static readonly RectangleF FullFrame = new(0, 0, Target.Width, Target.Height);
    private static readonly SnowSettings HeavySnow = new(DensityPct: 100, SizePct: 100);

    private static RegionSet RightHalf() =>
        new([new Region([new(0.5, 0), new(1, 0), new(1, 1), new(0.5, 1)])], Invert: false, FeatherPct: 0);

    private static WallpaperScene CreateScene(GpuDevice gpu, ImageQuadRenderer background) =>
        new(gpu, background, gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10)), FullFrame, FullFrame);

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
    public void NoEffects_FrameEqualsBackground()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var expected = new OffscreenTarget(gpu, Target);
        using var actual = new OffscreenTarget(gpu, Target);
        using var image = gpu.CreateImageTexture(OffscreenTarget.Solid(80, 30, 10));
        background.Draw(expected.View, Target, image, FullFrame);
        using var scene = CreateScene(gpu, background);

        scene.Update(new FrameTime(1.0 / 60, 1.0 / 60), Target);
        scene.Render(actual.View, Target);

        Assert.Equal(expected.ReadBack(), actual.ReadBack());
    }

    [Fact]
    public void RegionsForAnotherEffect_DoNotMaskSnow()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var plain = CreateScene(gpu, background);
        plain.Render(target.View, Target);
        var backgroundPixels = target.ReadBack();
        using var scene = CreateScene(gpu, background);
        var snow = new SnowEffect(gpu, 7);
        snow.Apply(HeavySnow);
        scene.Effects.Add(snow);

        scene.SetRegions(EffectKind.Rain, RightHalf());
        for (var i = 1; i <= Frames; i++)
        {
            scene.Update(new FrameTime(1.0 / 60, i / 60.0), Target);
        }
        scene.Render(target.View, Target);
        var pixels = target.ReadBack();

        Assert.True(Brightened(pixels, backgroundPixels, 0, 958) >= 1000);
        Assert.True(Brightened(pixels, backgroundPixels, 962, Target.Width) >= 1000);
    }

    [Fact]
    public void FrameCopy_MatchesTarget()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var image = gpu.CreateImageTexture(OffscreenTarget.Solid(200, 120, 40));
        background.Draw(target.View, Target, image, FullFrame);
        using var copy = new FrameCopy(gpu);

        var view = copy.Capture(target.View, Target);

        Assert.Equal(target.ReadBack(), target.ReadBack(view));
    }

    [Fact]
    public void Render_DrawsEffectsInFixedOrder()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = CreateScene(gpu, background);
        var drawn = new List<EffectKind>();
        foreach (var kind in new[] { EffectKind.Glass, EffectKind.Snow, EffectKind.Water, EffectKind.Rain })
        {
            scene.Effects.Add(new RecordingEffect(kind, needsFrameCopy: kind == EffectKind.Glass, drawn));
        }

        scene.Render(target.View, Target);

        Assert.Equal([EffectKind.Water, EffectKind.Rain, EffectKind.Snow, EffectKind.Glass], drawn);
    }

    [Fact]
    public void Render_GivesTheFrameCopyOnlyToEffectsThatNeedIt()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var target = new OffscreenTarget(gpu, Target);
        using var scene = CreateScene(gpu, background);
        var glass = new RecordingEffect(EffectKind.Glass, needsFrameCopy: true, []);
        var snow = new RecordingEffect(EffectKind.Snow, needsFrameCopy: false, []);
        scene.Effects.Add(glass);
        scene.Effects.Add(snow);

        scene.Render(target.View, Target);

        Assert.Equal((true, false), (glass.HadFrame, snow.HadFrame));
    }

    [Fact]
    public void SetGeometry_OnlyWhenRegionsOrTargetChange()
    {
        WindowsOnly.Require();
        using var gpu = GpuDevice.Create(useWarp: true);
        using var background = new ImageQuadRenderer(gpu);
        using var scene = CreateScene(gpu, background);
        var rain = new RecordingEffect(EffectKind.Rain, needsFrameCopy: false, []);
        scene.Effects.Add(rain);
        var frame = new FrameTime(1.0 / 60, 1.0 / 60);

        scene.Update(frame, Target);
        scene.Update(frame, Target);
        scene.SetRegions(EffectKind.Rain, RightHalf());
        scene.Update(frame, Target);
        scene.SetRegions(EffectKind.Rain, RightHalf());
        scene.Update(frame, Target);
        scene.SetRegions(EffectKind.Snow, RightHalf());
        scene.Update(frame, Target);
        scene.Update(frame, new Size(1280, 720));

        Assert.Equal(3, rain.GeometryCalls);
    }

    private sealed class RecordingEffect(EffectKind kind, bool needsFrameCopy, List<EffectKind> drawn) : IEffect
    {
        public EffectKind Kind => kind;

        public bool NeedsFrameCopy => needsFrameCopy;

        public int ActiveParticles => 0;

        public bool IsAnimated => false;

        public int GeometryCalls { get; private set; }

        public bool HadFrame { get; private set; }

        public void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels) => GeometryCalls++;

        public void Update(in FrameTime time, Size targetPixels)
        {
        }

        public void Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs)
        {
            drawn.Add(kind);
            HadFrame = inputs.Frame is not null;
        }

        public void Dispose()
        {
        }
    }
}
