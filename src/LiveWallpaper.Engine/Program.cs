using System.Text.Json;
using LiveWallpaper.Core;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;
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

        var item = new LibraryStore(AppPaths.LibraryDirectory, TimeProvider.System).Import(imagePath, size.Width, size.Height);
        var request = new ApplyWallpaper(item.Id, set.MonitorSelector, set.Scaling);
        var response = new EngineClient(EnginePipe.Name).SendAsync(request, TimeSpan.FromMilliseconds(500), CancellationToken.None).GetAwaiter().GetResult();
        switch (response)
        {
            case Ok:
                Console.WriteLine("Wallpaper set.");
                return 0;
            case Failed failed:
                return Fail(failed.Message, 1);
        }

        var store = new SettingsStore(AppPaths.SettingsFile, TimeProvider.System);
        var current = store.Load().Settings;
        var monitorIds = MonitorEnumerator.Enumerate().Select(m => m.Id).ToList();
        var assignments = MonitorAssignments.Apply(current.Monitors, monitorIds, item.Id, set.MonitorSelector, set.Scaling);
        if (assignments is null)
        {
            return Fail($"No monitor has the id '{set.MonitorSelector}'. Run --list-monitors to see the ids.", 1);
        }
        store.Save(current with { Monitors = assignments });
        Console.WriteLine("Wallpaper set. Start LiveWallpaper.Engine.exe to show it.");
        return 0;
    }

    private static string Describe(MonitorInfo m) =>
        $"{m.Id}  bounds={m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}  dpi={m.Dpi}  refresh={m.RefreshHz}Hz{(m.IsPrimary ? "  primary" : "")}";

    private static int Fail(string message, int exitCode)
    {
        Console.Error.WriteLine(message);
        return exitCode;
    }
}
