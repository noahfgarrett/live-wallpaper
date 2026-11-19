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
    private const int ResizeSettleMs = 150;
    private const int IdleSleepMs = 60;
    private const int OccludedSleepMs = 120;

    private readonly nint _window;
    private readonly Thread _thread;
    private readonly Lock _gate = new();
    private volatile bool _stop;
    private Size _size;
    private long _sizeChangedAt;
    private (string Path, WallpaperScaling Scaling, Size Reference)? _image;
    private int _imageVersion;
    private SnowSettings _snow = new();
    private RegionSet _regions = RegionSet.Empty;
    private OverlayFrame _overlay = new([], 1f);
    private volatile bool _thirtyFps;

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
            if (size != _size)
            {
                _size = size;
                _sizeChangedAt = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <param name="path">The image file.</param>
    /// <param name="scaling">How it is scaled.</param>
    /// <param name="reference">The monitor the preview stands for. Center scaling crops at its size, so the preview shows what the desktop will.</param>
    public void SetImage(string path, WallpaperScaling scaling, Size reference)
    {
        lock (_gate)
        {
            _image = (path, scaling, reference);
            _imageVersion++;
        }
    }

    public void SetSnow(SnowSettings settings) => Volatile.Write(ref _snow, settings);

    public void SetRegions(RegionSet regions) => Volatile.Write(ref _regions, regions);

    /// <summary>The editor's outlines and handles, in image coordinates; drawn over the scene with the scene's own image rectangle.</summary>
    public void SetOverlay(IReadOnlyList<OverlayShape> shapes, float scale) => Volatile.Write(ref _overlay, new OverlayFrame(shapes, scale));

    public void SetFrameRate(FrameRateMode mode) => _thirtyFps = mode == FrameRateMode.Fps30;

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
            WallpaperSurface? surface = null;
            WallpaperScene? scene = null;
            SnowEffect? snow = null;
            var builtVersion = -1;
            var builtSize = Size.Empty;
            var clock = Stopwatch.StartNew();
            var last = 0.0;
            try
            {
                while (!_stop)
                {
                    Size size;
                    long changedAt;
                    (string Path, WallpaperScaling Scaling, Size Reference)? image;
                    int version;
                    lock (_gate)
                    {
                        size = _size;
                        changedAt = _sizeChangedAt;
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

                    var settled = Stopwatch.GetElapsedTime(changedAt).TotalMilliseconds >= ResizeSettleMs;
                    if (image is { } wanted && (version != builtVersion || (builtSize != size && settled)))
                    {
                        scene?.Dispose();
                        scene = null;
                        snow = null;
                        try
                        {
                            (scene, snow) = BuildScene(gpu, background, wanted.Path, wanted.Scaling, wanted.Reference, size);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            Log.Error("preview", "event=scene-failed", ex);
                        }
                        builtVersion = version;
                        builtSize = size;
                    }

                    var now = clock.Elapsed.TotalSeconds;
                    var dt = Math.Clamp(now - last, 0, 0.1);
                    last = now;
                    if (scene is not null && snow is not null)
                    {
                        snow.Apply(Volatile.Read(ref _snow));
                        scene.SetRegions(Volatile.Read(ref _regions));
                        scene.Update(new FrameTime(dt, now), size);
                        scene.Render(surface.BackBuffer, size);
                        var frame = Volatile.Read(ref _overlay);
                        overlay.Draw(surface.BackBuffer, size, scene.ImageBounds, frame.Shapes, frame.Scale);
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

    /// <summary>Builds the scene as the reference monitor would show it, scaled down to the preview.</summary>
    private static (WallpaperScene Scene, SnowEffect Snow) BuildScene(GpuDevice gpu, ImageQuadRenderer background, string path, WallpaperScaling scaling, Size reference, Size target)
    {
        var imageSize = WicImageLoader.ReadSize(path);
        var crop = ScalingMath.ComputeSourceCrop(imageSize, reference, scaling);
        var onReference = ScalingMath.ComputeDecodeSize(imageSize, reference, scaling);
        var decodeSize = new Size(
            Math.Clamp((int)Math.Ceiling(onReference.Width * (double)target.Width / reference.Width), 1, crop.Width),
            Math.Clamp((int)Math.Ceiling(onReference.Height * (double)target.Height / reference.Height), 1, crop.Height));
        var decoded = WicImageLoader.Load(path, crop, decodeSize);
        var scene = new WallpaperScene(gpu, background, gpu.CreateImageTexture(decoded),
            ScalingMath.ComputeScaledDestination(crop.Size, reference, target, scaling),
            ScalingMath.ComputeScaledDestination(imageSize, reference, target, scaling));
        var snow = new SnowEffect(gpu, 20261119);
        scene.Effects.Add(snow);
        return (scene, snow);
    }

    private sealed record OverlayFrame(IReadOnlyList<OverlayShape> Shapes, float Scale);

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
