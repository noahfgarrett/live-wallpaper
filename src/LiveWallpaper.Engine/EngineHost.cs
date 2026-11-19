using System.Drawing;
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
    private readonly AttachRetryPolicy _retry = new();
    private readonly List<ActiveWallpaper> _wallpapers = [];
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private DesktopAttachPlan _plan = new DesktopAttachPlan.Unsupported("Not attached yet.");

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
            DesktopRestorer.RestoreDesktop();
        }
        marker.MarkRunning();
        Log.Info("engine", $"event=start version={typeof(EngineHost).Assembly.GetName().Version} os={Environment.OSVersion.Version}");

        using var gpu = CreateGpu();
        using var renderer = new ImageQuadRenderer(gpu);
        var host = new EngineHost(gpu, renderer, new SettingsStore(AppPaths.SettingsFile, TimeProvider.System));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => host.OnCrash(e.ExceptionObject as Exception);
        using var messages = new MessageWindow(host.OnEvent);

        var exitCode = 0;
        try
        {
            if (host.Build(isFirstBuild: true))
            {
                messages.StartAttachmentChecks(AttachmentCheckIntervalMs);
                messages.RunMessageLoop();
            }
            else
            {
                exitCode = 2;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("engine", "event=fatal", ex);
            exitCode = 3;
        }

        host.Teardown();
        if (DesktopHost.HasTouchedDesktop)
        {
            DesktopRestorer.RestoreDesktop();
        }
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

    private void OnCrash(Exception? ex)
    {
        Log.Error("engine", "event=crash", ex);
        try
        {
            if (Environment.CurrentManagedThreadId == _uiThreadId)
            {
                Teardown();
            }
            DesktopRestorer.RestoreDesktop();
        }
        catch (Exception cleanupError)
        {
            Log.Error("engine", "event=crash-cleanup-failed", cleanupError);
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
                var lost = _wallpapers.Any(w => !DesktopHost.IsStillAttached(w.Window.Handle, _plan, w.Monitor.Bounds));
                if (_retry.ShouldRebuildOnCheck(lost))
                {
                    Log.Info("engine", $"event=reattach windowLost={lost}");
                    Rebuild();
                }
                break;
            case MessageWindowEvent.CloseRequested:
                Log.Info("engine", "event=close-requested");
                MessageWindow.Quit();
                break;
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

        var prepared = PrepareImages(load.Settings.Monitors);
        if (prepared.Count == 0)
        {
            Log.Info("engine", $"event=nothing-to-show assignments={load.Settings.Monitors.Count}");
            _retry.RecordBuild(BuildOutcome.NothingToShow);
            return true;
        }

        _plan = DesktopHost.Prepare();
        if (_plan is DesktopAttachPlan.Unsupported unsupported)
        {
            DisposeAll(prepared);
            Log.Error("engine", $"event=attach-unsupported reason=\"{unsupported.Reason}\"");
            _retry.RecordBuild(BuildOutcome.AttachFailed);
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

        foreach (var image in prepared)
        {
            AttachAndShow(image);
        }
        _retry.RecordBuild(_wallpapers.Count > 0 ? BuildOutcome.Shown : BuildOutcome.AttachFailed);
        Log.Info("engine", $"event=built wallpapers={_wallpapers.Count} prepared={prepared.Count}");
        return true;
    }

    private List<PreparedImage> PrepareImages(IReadOnlyList<MonitorAssignment> assignments)
    {
        var prepared = new List<PreparedImage>();
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            var assignment = assignments.FirstOrDefault(a => string.Equals(a.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase));
            if (assignment is null)
            {
                continue;
            }
            try
            {
                var target = monitor.Bounds.Size;
                var imageSize = WicImageLoader.ReadSize(assignment.ImagePath);
                var crop = ScalingMath.ComputeSourceCrop(imageSize, target, assignment.Scaling);
                var image = WicImageLoader.Load(assignment.ImagePath, crop, ScalingMath.ComputeDecodeSize(imageSize, target, assignment.Scaling));
                var texture = _gpu.CreateImageTexture(image);
                prepared.Add(new PreparedImage(monitor, texture, ScalingMath.ComputeDestination(crop.Size, target, assignment.Scaling)));
                Log.Info("engine", $"event=decoded monitor=\"{monitor.DeviceName}\" image={imageSize.Width}x{imageSize.Height} decoded={image.Width}x{image.Height} scaling={assignment.Scaling}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("engine", $"event=monitor-skipped stage=decode monitor=\"{monitor.DeviceName}\"", ex);
            }
        }
        return prepared;
    }

    private void AttachAndShow(PreparedImage image)
    {
        WallpaperWindow? window = null;
        WallpaperSurface? surface = null;
        try
        {
            var target = image.Monitor.Bounds.Size;
            window = WallpaperWindow.Create();
            DesktopHost.Attach(window.Handle, _plan, image.Monitor.Bounds);
            surface = new WallpaperSurface(_gpu, window.Handle, target, useFlipModel: !DesktopHost.UsesLayeredWindow(_plan));
            window.ShowWithoutActivating();
            _renderer.Draw(surface.BackBuffer, target, image.Texture, image.Destination);
            surface.Present();
            _wallpapers.Add(new ActiveWallpaper(image.Monitor, window, surface, image.Texture));
            Log.Info("engine", $"event=shown monitor=\"{image.Monitor.DeviceName}\" size={target.Width}x{target.Height} flip={surface.UsesFlipModel}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("engine", $"event=monitor-skipped stage=attach monitor=\"{image.Monitor.DeviceName}\"", ex);
            surface?.Dispose();
            image.Texture.Dispose();
            window?.Dispose();
        }
    }

    private static void DisposeAll(List<PreparedImage> prepared)
    {
        foreach (var image in prepared)
        {
            image.Texture.Dispose();
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

    private sealed record PreparedImage(MonitorInfo Monitor, ID3D11ShaderResourceView Texture, RectangleF Destination);

    private sealed record ActiveWallpaper(MonitorInfo Monitor, WallpaperWindow Window, WallpaperSurface Surface, ID3D11ShaderResourceView Texture);
}
