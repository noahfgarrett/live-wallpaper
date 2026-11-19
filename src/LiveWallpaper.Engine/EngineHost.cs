using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using LiveWallpaper.Core;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Library;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Rendering;
using LiveWallpaper.Core.Settings;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;
using LiveWallpaper.Windows;
using SharpGen.Runtime;
using Vortice.Direct3D11;

namespace LiveWallpaper.Engine;

public sealed class EngineHost
{
    public const string AppExeName = "LiveWallpaper.exe";
    public const string IconFileName = "LiveWallpaper.ico";

    private const uint AttachmentCheckIntervalMs = 5000;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double PowerCheckIntervalSeconds = 2;
    private const int RecentItemCount = 8;

    private readonly GpuDevice _gpu;
    private readonly ImageQuadRenderer _background;
    private readonly SettingsStore _store = new(AppPaths.SettingsFile, TimeProvider.System);
    private readonly LibraryStore _library = new(AppPaths.LibraryDirectory, TimeProvider.System);
    private readonly AttachRetryPolicy _retry = new();
    private readonly List<ActiveWallpaper> _wallpapers = [];
    private readonly ConcurrentQueue<PendingRequest> _requests = new();
    private readonly EngineDiagnostics _diagnostics = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly RenderLoop _loop;
    private DesktopAttachPlan _plan = new DesktopAttachPlan.Unsupported("Not attached yet.");
    private AppSettings _settings = new();
    private TrayIcon? _tray;
    private string? _problem;
    private volatile bool _stopping;
    private bool _paused;
    private bool _needsPresent;
    private bool _onBattery;
    private double _lastFrameSeconds;
    private double _lastPowerCheckSeconds = double.NegativeInfinity;
    private double _refreshHz = 60;

    private EngineHost(GpuDevice gpu, ImageQuadRenderer background, RenderLoop loop)
    {
        _gpu = gpu;
        _background = background;
        _loop = loop;
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
        using var background = new ImageQuadRenderer(gpu);
        using var loop = new RenderLoop();
        var host = new EngineHost(gpu, background, loop);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => host.OnCrash(e.ExceptionObject as Exception);
        using var messages = new MessageWindow(host.OnEvent);
        using var tray = new TrayIcon(messages.Handle, Path.Combine(AppContext.BaseDirectory, IconFileName), "Live Wallpaper");
        host._tray = tray;
        messages.RawMessageHandler = host.OnRawMessage;
        tray.Show();

        var server = new EnginePipeServer(EnginePipe.Name, PipeSecurityFactory.CurrentUserOnly(EnginePipe.Name), host.EnqueueRequest);
        server.Start();

        var exitCode = 0;
        try
        {
            host.Build();
            messages.StartAttachmentChecks(AttachmentCheckIntervalMs);
            loop.Run(host.OnFrame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("engine", "event=fatal", ex);
            exitCode = 3;
        }

        host._stopping = true;
        host.FailPendingRequests();
        server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        host.FailPendingRequests();
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

    private Task<EngineResponse> EnqueueRequest(EngineRequest request)
    {
        if (_stopping)
        {
            return Task.FromResult<EngineResponse>(new Failed("The engine is shutting down."));
        }
        var pending = new PendingRequest(request, new TaskCompletionSource<EngineResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        _requests.Enqueue(pending);
        _loop.RequestFrame();
        return pending.Completion.Task;
    }

    private TimeSpan? OnFrame()
    {
        ProcessRequests();

        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastFrameSeconds, 0, MaxFrameDeltaSeconds);
        _lastFrameSeconds = now;

        var animated = !_paused && AnyAnimated();
        if (!animated && !_needsPresent)
        {
            return null;
        }

        try
        {
            var time = new FrameTime(dt, now);
            foreach (var wallpaper in _wallpapers)
            {
                var size = wallpaper.Monitor.Bounds.Size;
                if (animated)
                {
                    wallpaper.Scene.Update(time, size);
                }
                else if (!_paused)
                {
                    wallpaper.Scene.Update(new FrameTime(0, now), size);
                }
                wallpaper.Scene.Render(wallpaper.Surface.BackBuffer, size);
                wallpaper.Surface.Present(0);
            }
        }
        catch (SharpGenException ex)
        {
            Log.Error("engine", "event=present-failed action=rebuild", ex);
            Rebuild();
            return null;
        }

        _needsPresent = false;
        _diagnostics.RecordFrame(now);
        if (!animated)
        {
            return null;
        }
        if (now - _lastPowerCheckSeconds >= PowerCheckIntervalSeconds)
        {
            _onBattery = PowerStatus.IsOnBattery();
            _lastPowerCheckSeconds = now;
        }
        return FramePacer.IntervalFor(_settings.FrameRate, _refreshHz, _onBattery);
    }

    private bool AnyAnimated()
    {
        foreach (var wallpaper in _wallpapers)
        {
            if (wallpaper.Scene.IsAnimated)
            {
                return true;
            }
        }
        return false;
    }

    private void ProcessRequests()
    {
        while (_requests.TryDequeue(out var pending))
        {
            EngineResponse response;
            try
            {
                response = Handle(pending.Request);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("engine", $"event=request-failed type={pending.Request.GetType().Name}", ex);
                response = new Failed("The engine couldn't complete that request.");
            }
            pending.Completion.TrySetResult(response);
        }
    }

    private EngineResponse Handle(EngineRequest request)
    {
        switch (request)
        {
            case ApplyWallpaper apply:
                return ApplyItem(apply.LibraryItemId, apply.MonitorSelector, apply.Scaling);
            case UpdateSnow update:
                if (!update.Snow.IsValid)
                {
                    return new Failed("Snow settings must be between 0 and 100%.");
                }
                foreach (var wallpaper in _wallpapers.Where(w => w.ItemId == update.LibraryItemId))
                {
                    wallpaper.Snow.Apply(update.Snow);
                }
                _needsPresent = true;
                return new Ok();
            case UpdateRegions update:
                if (update.Regions is null || !update.Regions.IsValid)
                {
                    return new Failed("Those regions aren't valid.");
                }
                foreach (var wallpaper in _wallpapers.Where(w => w.ItemId == update.LibraryItemId))
                {
                    wallpaper.Scene.SetRegions(update.Regions);
                }
                _needsPresent = true;
                return new Ok();
            case SetPaused pause:
                _paused = pause.Paused;
                _needsPresent = true;
                return new Ok();
            case SetPreferences prefs:
                _settings = _settings with
                {
                    FrameRate = prefs.FrameRate,
                    DefaultScaling = prefs.DefaultScaling,
                    KeepRunningWhenAppCloses = prefs.KeepRunningWhenAppCloses,
                };
                _store.Save(_settings);
                return new Ok();
            case GetStatus:
                return new StatusReport(BuildStatus());
            case Reload:
                Rebuild();
                return new Ok();
            case Shutdown:
                Log.Info("engine", "event=shutdown-requested");
                _loop.Stop();
                return new Ok();
            default:
                return new Failed("Unknown request.");
        }
    }

    private EngineResponse ApplyItem(Guid itemId, string selector, WallpaperScaling scaling)
    {
        if (_library.LoadReadOnly().All(i => i.Id != itemId))
        {
            return new Failed("That image is no longer in the library.");
        }
        var monitorIds = MonitorEnumerator.Enumerate().Select(m => m.Id).ToList();
        var next = MonitorAssignments.Apply(_settings.Monitors, monitorIds, itemId, selector, scaling);
        if (next is null)
        {
            return new Failed("That monitor isn't connected.");
        }
        _settings = _settings with { Monitors = next };
        _store.Save(_settings);
        Rebuild();

        var targets = string.Equals(selector, MonitorAssignments.AllMonitors, StringComparison.OrdinalIgnoreCase)
            ? monitorIds
            : monitorIds.Where(id => string.Equals(id, selector, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!targets.Any(id => _wallpapers.Any(w => w.Monitor.Id == id && w.ItemId == itemId)))
        {
            return new Failed($"Saved, but it couldn't be shown on your desktop. {_problem ?? "The details are in the engine log."}");
        }
        return new Ok();
    }

    private EngineStatus BuildStatus()
    {
        var (privateBytes, cpu) = _diagnostics.SampleProcess();
        var monitors = MonitorEnumerator.Enumerate()
            .Select(m =>
            {
                var assignment = _settings.Monitors.FirstOrDefault(a => string.Equals(a.MonitorId, m.Id, StringComparison.OrdinalIgnoreCase));
                return new MonitorStatus(m.Id, m.DeviceName, m.Bounds.Width, m.Bounds.Height, m.IsPrimary,
                    assignment?.LibraryItemId, assignment?.Scaling, _wallpapers.Any(w => w.Monitor.Id == m.Id));
            })
            .ToList();
        var particles = _wallpapers.Sum(w => w.Scene.ActiveParticles);
        return new EngineStatus(_paused, _diagnostics.Fps(_clock.Elapsed.TotalSeconds), privateBytes, cpu, particles, monitors, _settings, _problem);
    }

    private bool OnRawMessage(uint msg, nuint wParam, nint lParam)
    {
        if (msg != TrayIcon.CallbackMessage || _tray is null)
        {
            return false;
        }
        IReadOnlyList<(Guid Id, string Name)> Recent() => _library.LoadReadOnly()
            .OrderByDescending(i => i.ImportedAt)
            .Take(RecentItemCount)
            .Select(i => (i.Id, i.DisplayName))
            .ToList();
        switch (_tray.HandleCallback(lParam, _paused, Recent, out var recentId))
        {
            case TrayCommand.OpenApp:
                LaunchApp(string.Empty);
                break;
            case TrayCommand.OpenSettings:
                LaunchApp("--page settings");
                break;
            case TrayCommand.PauseResume:
                _paused = !_paused;
                _needsPresent = true;
                _loop.RequestFrame();
                break;
            case TrayCommand.ApplyRecent:
                ApplyItem(recentId, MonitorAssignments.AllMonitors, _settings.DefaultScaling);
                _loop.RequestFrame();
                break;
            case TrayCommand.Exit:
                Log.Info("engine", "event=exit-from-tray");
                AppSignals.RequestAppExit();
                _loop.Stop();
                break;
        }
        return true;
    }

    private static void LaunchApp(string arguments)
    {
        var path = Path.Combine(AppContext.BaseDirectory, AppExeName);
        if (!File.Exists(path))
        {
            Log.Error("engine", "event=app-missing");
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("engine", "event=app-launch-failed", ex);
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
                _tray?.Show();
                Log.Info("engine", $"event=rebuild reason={e}");
                Rebuild();
                break;
            case MessageWindowEvent.DisplayChanged:
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
                _loop.Stop();
                break;
        }
    }

    private void Rebuild()
    {
        var previouslyShown = _wallpapers.Select(w => w.Monitor.Id).ToList();
        Teardown();
        Build();
        if (previouslyShown.Any(id => _wallpapers.All(w => w.Monitor.Id != id)))
        {
            DesktopRestorer.RestoreDesktop();
        }
        _loop.RequestFrame();
    }

    private void Build()
    {
        var load = _store.Load();
        if (load.Outcome == SettingsLoadOutcome.CorruptReplaced)
        {
            Log.Error("engine", $"event=settings-corrupt backup=\"{Path.GetFileName(load.BackupPath)}\"");
        }
        _settings = load.Settings;
        _problem = null;

        var library = _library.LoadReadOnly();
        var pruned = _settings.Monitors.Where(a => library.Any(i => i.Id == a.LibraryItemId)).ToList();
        if (pruned.Count != _settings.Monitors.Count)
        {
            _settings = _settings with { Monitors = pruned };
            _store.Save(_settings);
            Log.Info("engine", $"event=pruned-assignments removed={load.Settings.Monitors.Count - pruned.Count}");
        }

        var monitors = MonitorEnumerator.Enumerate();
        _refreshHz = monitors.FirstOrDefault(m => m.IsPrimary)?.RefreshHz ?? 60;
        var prepared = PrepareImages(monitors, _settings.Monitors, library);
        if (prepared.Count == 0)
        {
            Log.Info("engine", $"event=nothing-to-show assignments={_settings.Monitors.Count}");
            _retry.RecordBuild(BuildOutcome.NothingToShow);
            return;
        }

        _plan = DesktopHost.Prepare();
        if (_plan is DesktopAttachPlan.Unsupported unsupported)
        {
            foreach (var image in prepared)
            {
                image.Texture.Dispose();
            }
            _problem = $"Live Wallpaper couldn't attach to your desktop. {unsupported.Reason}";
            Log.Error("engine", $"event=attach-unsupported reason=\"{unsupported.Reason}\"");
            _retry.RecordBuild(BuildOutcome.AttachFailed);
            return;
        }

        foreach (var image in prepared)
        {
            AttachAndShow(image);
        }
        _needsPresent = true;
        _retry.RecordBuild(_wallpapers.Count > 0 ? BuildOutcome.Shown : BuildOutcome.AttachFailed);
        Log.Info("engine", $"event=built wallpapers={_wallpapers.Count} prepared={prepared.Count}");
    }

    private List<PreparedImage> PrepareImages(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<MonitorAssignment> assignments, IReadOnlyList<LibraryItem> library)
    {
        var prepared = new List<PreparedImage>();
        foreach (var monitor in monitors)
        {
            var assignment = assignments.FirstOrDefault(a => string.Equals(a.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase));
            if (assignment is null)
            {
                continue;
            }
            var item = library.FirstOrDefault(i => i.Id == assignment.LibraryItemId);
            if (item is null)
            {
                Log.Info("engine", $"event=monitor-skipped reason=library-item-missing monitor=\"{monitor.DeviceName}\"");
                continue;
            }
            try
            {
                var target = monitor.Bounds.Size;
                var imagePath = _library.ImagePath(item);
                var imageSize = WicImageLoader.ReadSize(imagePath);
                var crop = ScalingMath.ComputeSourceCrop(imageSize, target, assignment.Scaling);
                var image = WicImageLoader.Load(imagePath, crop, ScalingMath.ComputeDecodeSize(imageSize, target, assignment.Scaling));
                var texture = _gpu.CreateImageTexture(image);
                prepared.Add(new PreparedImage(monitor, item, texture,
                    ScalingMath.ComputeDestination(crop.Size, target, assignment.Scaling),
                    ScalingMath.ComputeDestination(imageSize, target, assignment.Scaling)));
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
        WallpaperScene? scene = null;
        try
        {
            var target = image.Monitor.Bounds.Size;
            scene = new WallpaperScene(_gpu, _background, image.Texture, image.Destination, image.ImageBounds);
            var snow = new SnowEffect(_gpu, Seed(image.Monitor.Id));
            scene.Effects.Add(snow);
            snow.Apply(image.Item.Snow);
            scene.SetRegions(image.Item.Regions);

            window = WallpaperWindow.Create();
            DesktopHost.Attach(window.Handle, _plan, image.Monitor.Bounds);
            surface = new WallpaperSurface(_gpu, window.Handle, target, useFlipModel: !DesktopHost.UsesLayeredWindow(_plan));
            window.ShowWithoutActivating();
            scene.Update(new FrameTime(0, _clock.Elapsed.TotalSeconds), target);
            scene.Render(surface.BackBuffer, target);
            surface.Present(0);
            _wallpapers.Add(new ActiveWallpaper(image.Monitor, image.Item.Id, window, surface, scene, snow));
            Log.Info("engine", $"event=shown monitor=\"{image.Monitor.DeviceName}\" size={target.Width}x{target.Height} flip={surface.UsesFlipModel}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("engine", $"event=monitor-skipped stage=attach monitor=\"{image.Monitor.DeviceName}\"", ex);
            surface?.Dispose();
            if (scene is null)
            {
                image.Texture.Dispose();
            }
            else
            {
                scene.Dispose();
            }
            window?.Dispose();
        }
    }

    private void Teardown()
    {
        foreach (var wallpaper in _wallpapers)
        {
            wallpaper.Surface.Dispose();
            wallpaper.Scene.Dispose();
            wallpaper.Window.Dispose();
        }
        _wallpapers.Clear();
        _gpu.Context.ClearState();
        _gpu.Context.Flush();
    }

    private void FailPendingRequests()
    {
        while (_requests.TryDequeue(out var pending))
        {
            pending.Completion.TrySetResult(new Failed("The engine is shutting down."));
        }
    }

    private static uint Seed(string monitorId)
    {
        var hash = 2166136261u;
        foreach (var c in monitorId)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }
        return hash;
    }

    private sealed record PendingRequest(EngineRequest Request, TaskCompletionSource<EngineResponse> Completion);

    private sealed record PreparedImage(MonitorInfo Monitor, LibraryItem Item, ID3D11ShaderResourceView Texture, RectangleF Destination, RectangleF ImageBounds);

    private sealed record ActiveWallpaper(MonitorInfo Monitor, Guid ItemId, WallpaperWindow Window, WallpaperSurface Surface, WallpaperScene Scene, SnowEffect Snow);
}
