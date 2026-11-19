using LiveWallpaper.Core;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;
using LiveWallpaper.Windows;
using SharpGen.Runtime;
using Vortice.Direct3D11;

namespace LiveWallpaper.Engine;

public sealed class EngineHost
{
    private const uint AttachmentCheckIntervalMs = 5000;

    private readonly GpuDevice _gpu;
    private readonly ImageQuadRenderer _renderer;
    private readonly SettingsStore _store;
    private readonly List<ActiveWallpaper> _wallpapers = [];
    private DesktopAttachPlan _plan = new DesktopAttachPlan.Unsupported("Not attached yet.");
    private bool _hasAssignments;

    private EngineHost(GpuDevice gpu, ImageQuadRenderer renderer, SettingsStore store)
    {
        _gpu = gpu;
        _renderer = renderer;
        _store = store;
    }

    public static int Run()
    {
        using var instance = SingleInstance.TryAcquire(SingleInstance.EngineMutexName);
        if (instance is null)
        {
            Log.Info("engine", "event=already-running");
            return 0;
        }

        var marker = new UncleanShutdownMarker(AppPaths.RunningMarkerFile);
        if (marker.WasUncleanShutdown())
        {
            Log.Info("engine", "event=recover-unclean-shutdown");
            DesktopRestorer.RepaintWallpaper();
        }
        marker.MarkRunning();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("engine", "event=crash", e.ExceptionObject as Exception);
            DesktopRestorer.RepaintWallpaper();
            marker.MarkStopped();
        };
        Log.Info("engine", $"event=start version={typeof(EngineHost).Assembly.GetName().Version} os={Environment.OSVersion.Version}");

        using var gpu = CreateGpu();
        using var renderer = new ImageQuadRenderer(gpu);
        var host = new EngineHost(gpu, renderer, new SettingsStore(AppPaths.SettingsFile, TimeProvider.System));
        using var messages = new MessageWindow(host.OnEvent);

        var exitCode = 0;
        if (host.Build(isFirstBuild: true))
        {
            messages.StartAttachmentChecks(AttachmentCheckIntervalMs);
            messages.RunMessageLoop();
        }
        else
        {
            exitCode = 2;
        }

        host.Teardown();
        DesktopRestorer.RepaintWallpaper();
        marker.MarkStopped();
        Log.Info("engine", $"event=stop exitCode={exitCode}");
        return exitCode;
    }

    private static GpuDevice CreateGpu()
    {
        try
        {
            return GpuDevice.Create();
        }
        catch (SharpGenException ex)
        {
            Log.Error("engine", "event=gpu-hardware-unavailable fallback=warp", ex);
            return GpuDevice.Create(useWarp: true);
        }
    }

    private void OnEvent(MessageWindowEvent e)
    {
        switch (e)
        {
            case MessageWindowEvent.ExplorerRestarted:
            case MessageWindowEvent.DisplayChanged:
            case MessageWindowEvent.ReloadRequested:
                Log.Info("engine", $"event=rebuild reason={e}");
                Rebuild();
                break;
            case MessageWindowEvent.AttachmentCheckDue:
                CheckAttachment();
                break;
            case MessageWindowEvent.CloseRequested:
                Log.Info("engine", "event=close-requested");
                MessageWindow.Quit();
                break;
        }
    }

    private void CheckAttachment()
    {
        var lost = _wallpapers.Any(w => !DesktopHost.IsStillAttached(w.Window.Handle, _plan));
        var neverAttached = _wallpapers.Count == 0 && _hasAssignments;
        if (lost || neverAttached)
        {
            Log.Info("engine", $"event=reattach lost={lost} neverAttached={neverAttached}");
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Teardown();
        Build(isFirstBuild: false);
    }

    private bool Build(bool isFirstBuild)
    {
        var load = _store.Load();
        if (load.Outcome == SettingsLoadOutcome.CorruptReplaced)
        {
            Log.Error("engine", $"event=settings-corrupt backup=\"{Path.GetFileName(load.BackupPath)}\"");
        }
        var assignments = load.Settings.Monitors;
        _hasAssignments = assignments.Count > 0;
        if (!_hasAssignments)
        {
            Log.Info("engine", "event=no-assignments");
            return true;
        }

        _plan = DesktopHost.Prepare();
        if (_plan is DesktopAttachPlan.Unsupported unsupported)
        {
            Log.Error("engine", $"event=attach-unsupported reason=\"{unsupported.Reason}\"");
            if (isFirstBuild)
            {
                UserMessages.ShowWarning(
                    "Live Wallpaper couldn't attach to your desktop, so nothing was changed.\n\n" +
                    $"{unsupported.Reason}\n\n" +
                    "Run \"LiveWallpaper.Engine.exe --dump-desktop desktop.json\" and share the file to help fix this.");
                return false;
            }
            return true;
        }

        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            var assignment = assignments.FirstOrDefault(a => string.Equals(a.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase));
            if (assignment is not null)
            {
                TryShow(monitor, assignment);
            }
        }
        Log.Info("engine", $"event=built wallpapers={_wallpapers.Count} assignments={assignments.Count}");
        return true;
    }

    private void TryShow(MonitorInfo monitor, MonitorAssignment assignment)
    {
        WallpaperWindow? window = null;
        WallpaperSurface? surface = null;
        ID3D11ShaderResourceView? texture = null;
        try
        {
            var imageSize = WicImageLoader.ReadSize(assignment.ImagePath);
            var target = monitor.Bounds.Size;
            var image = WicImageLoader.Load(assignment.ImagePath, ScalingMath.ComputeDecodeSize(imageSize, target, assignment.Scaling));
            texture = _gpu.CreateImageTexture(image);

            window = WallpaperWindow.Create();
            DesktopHost.Attach(window.Handle, _plan, monitor.Bounds);
            surface = new WallpaperSurface(_gpu, window.Handle, target, useFlipModel: !DesktopHost.UsesLayeredWindow(_plan));
            window.ShowWithoutActivating();
            _renderer.Draw(surface.BackBuffer, target, texture, ScalingMath.ComputeDestination(imageSize, target, assignment.Scaling));
            surface.Present();

            _wallpapers.Add(new ActiveWallpaper(monitor, window, surface, texture));
            Log.Info("engine", $"event=shown monitor=\"{monitor.DeviceName}\" size={target.Width}x{target.Height} decoded={image.Width}x{image.Height} scaling={assignment.Scaling} flip={surface.UsesFlipModel}");
        }
        catch (Exception ex) when (ex is ImageLoadException or InvalidOperationException or SharpGenException)
        {
            Log.Error("engine", $"event=monitor-skipped monitor=\"{monitor.DeviceName}\"", ex);
            surface?.Dispose();
            texture?.Dispose();
            window?.Dispose();
        }
    }

    private void Teardown()
    {
        foreach (var wallpaper in _wallpapers)
        {
            wallpaper.Surface.Dispose();
            wallpaper.Texture.Dispose();
            wallpaper.Window.Dispose();
        }
        _wallpapers.Clear();
        _gpu.Context.ClearState();
        _gpu.Context.Flush();
    }

    private sealed record ActiveWallpaper(MonitorInfo Monitor, WallpaperWindow Window, WallpaperSurface Surface, ID3D11ShaderResourceView Texture);
}
