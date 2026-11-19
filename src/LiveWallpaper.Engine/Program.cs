using System.Text.Json;
using LiveWallpaper.Core;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Library;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;
using LiveWallpaper.Media;
using LiveWallpaper.Windows;

namespace LiveWallpaper.Engine;

public static class Program
{
    private const int StopWaitAttempts = 50;
    private const int StopWaitIntervalMs = 100;

    [STAThread]
    public static int Main(string[] args)
    {
        var isDebug = Environment.GetEnvironmentVariable("LIVEWALLPAPER_DEBUG") == "1";
        Log.Initialize(new RollingLog(AppPaths.LogDirectory, "engine"), isDebug);

        var command = EngineCommandParser.Parse(args);
        if (command is not EngineCommand.Run)
        {
            var hasConsole = ConsoleOutput.AttachToParent();
            if (command is EngineCommand.Invalid invalid && !hasConsole)
            {
                UserMessages.ShowWarning(invalid.Message);
                return 2;
            }
        }

        return command switch
        {
            EngineCommand.Run => EngineHost.Run(),
            EngineCommand.Stop => Stop(),
            EngineCommand.ListMonitors => ListMonitors(),
            EngineCommand.DumpDesktop dump => DumpDesktop(dump.OutputPath),
            EngineCommand.SetWallpaper set => SetWallpaper(set),
            EngineCommand.Invalid invalid => Fail(invalid.Message, 2),
            _ => Fail("Unknown command.", 2),
        };
    }

    private static int Stop()
    {
        var window = MessageWindow.FindRunning();
        if (window == 0)
        {
            Console.WriteLine("Live Wallpaper isn't running.");
            return 0;
        }
        MessageWindow.PostClose(window);
        for (var i = 0; i < StopWaitAttempts && MessageWindow.FindRunning() != 0; i++)
        {
            Thread.Sleep(StopWaitIntervalMs);
        }
        Console.WriteLine(MessageWindow.FindRunning() == 0 ? "Live Wallpaper stopped." : "Live Wallpaper is still shutting down.");
        return 0;
    }

    private static int ListMonitors()
    {
        foreach (var m in MonitorEnumerator.Enumerate())
        {
            Console.WriteLine(Describe(m));
        }
        return 0;
    }

    private static int DumpDesktop(string outputPath)
    {
        var plan = DesktopHost.Prepare();
        var dump = new DesktopDump(
            plan.ToString(),
            MonitorEnumerator.Enumerate().Select(Describe).ToList(),
            DesktopSnapshotter.Capture());
        var fullPath = Path.GetFullPath(outputPath);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(dump, CoreJsonContext.Default.DesktopDump));
        Console.WriteLine($"Desktop layout: {plan}");
        Console.WriteLine($"Saved to {fullPath}");
        return 0;
    }

    private static int SetWallpaper(EngineCommand.SetWallpaper set)
    {
        var imagePath = Path.GetFullPath(set.ImagePath);
        System.Drawing.Size size;
        try
        {
            size = WicImageLoader.ReadSize(imagePath);
        }
        catch (ImageLoadException ex)
        {
            return Fail(ex.Message, 1);
        }

        var monitors = MonitorEnumerator.Enumerate();
        var item = new LibraryStore(AppPaths.LibraryDirectory, TimeProvider.System).Import(imagePath, size.Width, size.Height);
        var store = new SettingsStore(AppPaths.SettingsFile, TimeProvider.System);
        var current = store.Load().Settings;

        IReadOnlyList<MonitorAssignment> assignments;
        if (string.Equals(set.MonitorSelector, EngineCommandParser.AllMonitors, StringComparison.OrdinalIgnoreCase))
        {
            assignments = monitors.Select(m => new MonitorAssignment(m.Id, item.Id, set.Scaling)).ToList();
        }
        else
        {
            var monitor = monitors.FirstOrDefault(m => string.Equals(m.Id, set.MonitorSelector, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                return Fail($"No monitor has the id '{set.MonitorSelector}'. Run --list-monitors to see the ids.", 1);
            }
            assignments = current.Monitors
                .Where(a => !string.Equals(a.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase))
                .Append(new MonitorAssignment(monitor.Id, item.Id, set.Scaling))
                .ToList();
        }

        store.Save(current with { Monitors = assignments });
        var running = MessageWindow.FindRunning();
        if (running != 0)
        {
            MessageWindow.PostReload(running);
        }
        Console.WriteLine($"Wallpaper set on {(assignments.Count == 1 ? "1 monitor" : $"{assignments.Count} monitors")}.{(running == 0 ? " Start LiveWallpaper.Engine.exe to show it." : "")}");
        return 0;
    }

    private static string Describe(MonitorInfo m) =>
        $"{m.Id}  bounds={m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}  dpi={m.Dpi}{(m.IsPrimary ? "  primary" : "")}";

    private static int Fail(string message, int exitCode)
    {
        Console.Error.WriteLine(message);
        return exitCode;
    }
}
