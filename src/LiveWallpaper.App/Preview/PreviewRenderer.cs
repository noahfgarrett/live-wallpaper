using System.Diagnostics;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;
using SharpGen.Runtime;
using Vortice.Mathematics;
using Size = System.Drawing.Size;

namespace LiveWallpaper.App.Preview;

public sealed class PreviewRenderer : IDisposable
{
    private const int IdleSleepMs = 60;
    private const int OccludedSleepMs = 120;
    private const uint Seed = 20261119;

    private readonly nint _window;
    private readonly Thread _thread;
    private readonly Lock _gate = new();
    private volatile bool _stop;
    private Size _size;
    private (string Path, WallpaperScaling Scaling, Size Reference)? _image;
    private int _imageVersion;
    private SnowSettings _snow = new();
    private RainSettings _rain = new();
    private GlassSettings _glass = new();
    private WaterSettings _water = new();
    private RegionSet _snowRegions = RegionSet.Empty;
    private RegionSet _rainRegions = RegionSet.Empty;
    private RegionSet _glassRegions = RegionSet.Empty;
    private RegionSet _waterRegions = RegionSet.Empty;
    private OverlayFrame _overlay = new([], 1f);
    private volatile bool _thirtyFps;
    private PreviewCamera? _camera;

    public PreviewRenderer(nint window, Size size)
    {
        _window = window;
        _size = size;
        _thread = new Thread(Run) { IsBackground = true, Name = "Live Wallpaper preview" };
        _thread.Start();
    }

    public void Resize(Size size)
    {
        lock (_gate)
        {
            _size = size;
        }
    }

    /// <param name="path">The image file.</param>
    /// <param name="scaling">How it is scaled.</param>
    /// <param name="reference">The monitor the preview stands for. The scene is built at its resolution (<see cref="PreviewCamera.RenderSizeFor"/>), as the desktop builds it.</param>
    public void SetImage(string path, WallpaperScaling scaling, Size reference)
    {
        lock (_gate)
        {
            _image = (path, scaling, reference);
            _imageVersion++;
        }
    }

    public void SetSnow(SnowSettings settings) => Volatile.Write(ref _snow, settings);

    public void SetRain(RainSettings settings) => Volatile.Write(ref _rain, settings);

    public void SetGlass(GlassSettings settings) => Volatile.Write(ref _glass, settings);

    public void SetWater(WaterSettings settings) => Volatile.Write(ref _water, settings);

    public void SetRegions(EffectKind kind, RegionSet regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        switch (kind)
        {
            case EffectKind.Snow:
                Volatile.Write(ref _snowRegions, regions);
                break;
            case EffectKind.Rain:
                Volatile.Write(ref _rainRegions, regions);
                break;
            case EffectKind.Glass:
                Volatile.Write(ref _glassRegions, regions);
                break;
            case EffectKind.Water:
                Volatile.Write(ref _waterRegions, regions);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown effect.");
        }
    }

    /// <summary>The editor's outlines and handles, in image coordinates; drawn over the scene with the scene's own image rectangle.</summary>
    public void SetOverlay(IReadOnlyList<OverlayShape> shapes, float scale) => Volatile.Write(ref _overlay, new OverlayFrame(shapes, scale));

    public void SetFrameRate(FrameRateMode mode) => _thirtyFps = mode == FrameRateMode.Fps30;

    /// <summary>Which part of the desktop-sized scene the preview shows. Until one for the current image arrives, the preview fits.</summary>
    public void SetCamera(PreviewCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Volatile.Write(ref _camera, camera);
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        try
        {
            using var gpu = CreateGpu();
            using var background = new ImageQuadRenderer(gpu);
            using var overlay = new RegionOverlayRenderer(gpu);
            using var presenter = new PreviewPresenter(gpu);
            WallpaperSurface? surface = null;
            WallpaperScene? scene = null;
            SceneEffects? effects = null;
            var builtVersion = -1;
            var render = Size.Empty;
            PreviewCamera? requested = null;
            PreviewCamera? camera = null;
            var clock = Stopwatch.StartNew();
            var last = 0.0;
            try
            {
                while (!_stop)
                {
                    Size size;
                    (string Path, WallpaperScaling Scaling, Size Reference)? image;
                    int version;
                    lock (_gate)
                    {
                        size = _size;
                        image = _image;
                        version = _imageVersion;
                    }
                    if (size.Width < 2 || size.Height < 2)
                    {
                        Thread.Sleep(IdleSleepMs);
                        continue;
                    }

                    if (surface is null)
                    {
                        surface = new WallpaperSurface(gpu, _window, size, useFlipModel: true);
                    }
                    else if (surface.PixelSize != size)
                    {
                        surface.Resize(size);
                    }

                    if (image is { } wanted && version != builtVersion)
                    {
                        scene?.Dispose();
                        scene = null;
                        effects = null;
                        render = PreviewCamera.RenderSizeFor(wanted.Reference);
                        try
                        {
                            (scene, effects) = BuildScene(gpu, background, wanted.Path, wanted.Scaling, render);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            Log.Error("preview", "event=scene-failed", ex);
                        }
                        builtVersion = version;
                    }

                    var now = clock.Elapsed.TotalSeconds;
                    var dt = Math.Clamp(now - last, 0, 0.1);
                    last = now;
                    if (scene is not null && effects is not null)
                    {
                        var latest = Volatile.Read(ref _camera);
                        if (camera is null || !ReferenceEquals(latest, requested) || camera.Render != render || camera.View != size)
                        {
                            requested = latest;
                            camera = CameraFor(latest, render, size);
                        }
                        effects.Snow.Apply(Volatile.Read(ref _snow));
                        effects.Rain.Apply(Volatile.Read(ref _rain));
                        effects.Glass.Apply(Volatile.Read(ref _glass));
                        effects.Water.Apply(Volatile.Read(ref _water));
                        scene.SetRegions(EffectKind.Snow, Volatile.Read(ref _snowRegions));
                        scene.SetRegions(EffectKind.Rain, Volatile.Read(ref _rainRegions));
                        scene.SetRegions(EffectKind.Glass, Volatile.Read(ref _glassRegions));
                        scene.SetRegions(EffectKind.Water, Volatile.Read(ref _waterRegions));
                        scene.Update(new FrameTime(dt, now), render);
                        scene.Render(presenter.Begin(render), render);
                        presenter.Present(surface.BackBuffer, size, camera);
                        var frame = Volatile.Read(ref _overlay);
                        overlay.Draw(surface.BackBuffer, size, camera.MapRect(scene.ImageBounds), frame.Shapes, frame.Scale);
                    }
                    else
                    {
                        gpu.Context.ClearRenderTargetView(surface.BackBuffer, new Color4(0.05f, 0.08f, 0.13f, 1f));
                    }
                    if (!surface.Present(_thirtyFps ? 2u : 1u))
                    {
                        Thread.Sleep(OccludedSleepMs);
                    }
                }
            }
            finally
            {
                scene?.Dispose();
                surface?.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("preview", "event=renderer-stopped", ex);
        }
    }

    /// <summary>
    /// The camera to draw this frame with: the editor's, kept to the swap chain's size if the editor hasn't caught up with a
    /// resize yet, or Fit while there is none for this render size.
    /// </summary>
    private static PreviewCamera CameraFor(PreviewCamera? requested, Size render, Size view)
    {
        if (requested is null || requested.Render != render)
        {
            return PreviewCamera.Fit(render, view);
        }
        return requested.View == view ? requested : requested.Resize(view);
    }

    /// <summary>Builds the scene for a monitor of <paramref name="render"/> pixels, exactly as the engine builds it.</summary>
    private static (WallpaperScene Scene, SceneEffects Effects) BuildScene(GpuDevice gpu, ImageQuadRenderer background, string path, WallpaperScaling scaling, Size render)
    {
        var imageSize = WicImageLoader.ReadSize(path);
        var crop = ScalingMath.ComputeSourceCrop(imageSize, render, scaling);
        var decoded = WicImageLoader.Load(path, crop, ScalingMath.ComputeDecodeSize(imageSize, render, scaling));
        var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(decoded),
            ScalingMath.ComputeDestination(crop.Size, render, scaling),
            ScalingMath.ComputeDestination(imageSize, render, scaling));
        try
        {
            var snow = new SnowEffect(gpu, Seed);
            scene.Effects.Add(snow);
            var rain = new RainEffect(gpu, Seed ^ 0x9e3779b9);
            scene.Effects.Add(rain);
            var glass = new GlassEffect(gpu);
            scene.Effects.Add(glass);
            var water = new WaterEffect(gpu);
            scene.Effects.Add(water);
            return (scene, new SceneEffects(snow, rain, glass, water));
        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }

    private sealed record OverlayFrame(IReadOnlyList<OverlayShape> Shapes, float Scale);

    private sealed record SceneEffects(SnowEffect Snow, RainEffect Rain, GlassEffect Glass, WaterEffect Water);

    private static GpuDevice CreateGpu()
    {
        try
        {
            return GpuDevice.Create();
        }
        catch (SharpGenException)
        {
            return GpuDevice.Create(useWarp: true);
        }
    }
}
