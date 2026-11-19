using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core;

public abstract record EngineCommand
{
    private EngineCommand()
    {
    }

    public sealed record Run : EngineCommand;

    public sealed record Stop : EngineCommand;

    public sealed record ListMonitors : EngineCommand;

    public sealed record DumpDesktop(string OutputPath) : EngineCommand;

    public sealed record SetWallpaper(string ImagePath, string MonitorSelector, WallpaperScaling Scaling) : EngineCommand;

    public sealed record Invalid(string Message) : EngineCommand;
}

public static class EngineCommandParser
{
    public const string AllMonitors = "all";

    public const string Usage =
        "Usage: LiveWallpaper.Engine.exe [--set <image> [--monitor <id>|all] [--scaling fill|fit|stretch|center]] [--stop] [--list-monitors] [--dump-desktop <file>]";

    public static EngineCommand Parse(IReadOnlyList<string> args)
    {
        EngineCommand? verb = null;
        string? monitor = null;
        WallpaperScaling? scaling = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--stop":
                case "--list-monitors":
                    if (verb is not null)
                    {
                        return TooManyVerbs();
                    }
                    verb = arg == "--stop" ? new EngineCommand.Stop() : new EngineCommand.ListMonitors();
                    break;
                case "--dump-desktop":
                case "--set":
                    if (verb is not null)
                    {
                        return TooManyVerbs();
                    }
                    if (i + 1 >= args.Count)
                    {
                        return new EngineCommand.Invalid($"{arg} needs a file path. {Usage}");
                    }
                    var path = args[++i];
                    verb = arg == "--set"
                        ? new EngineCommand.SetWallpaper(path, AllMonitors, WallpaperScaling.Fill)
                        : new EngineCommand.DumpDesktop(path);
                    break;
                case "--monitor":
                    if (i + 1 >= args.Count)
                    {
                        return new EngineCommand.Invalid($"--monitor needs a monitor id or 'all'. {Usage}");
                    }
                    monitor = args[++i];
                    break;
                case "--scaling":
                    if (i + 1 >= args.Count || !TryParseScaling(args[i + 1], out var parsed))
                    {
                        return new EngineCommand.Invalid($"--scaling must be fill, fit, stretch or center. {Usage}");
                    }
                    scaling = parsed;
                    i++;
                    break;
                default:
                    return new EngineCommand.Invalid($"Unknown argument '{arg}'. {Usage}");
            }
        }

        if (verb is EngineCommand.SetWallpaper set)
        {
            return set with { MonitorSelector = monitor ?? AllMonitors, Scaling = scaling ?? WallpaperScaling.Fill };
        }
        if (monitor is not null || scaling is not null)
        {
            return new EngineCommand.Invalid($"--monitor and --scaling only apply to --set. {Usage}");
        }
        return verb ?? new EngineCommand.Run();
    }

    private static bool TryParseScaling(string value, out WallpaperScaling scaling) =>
        Enum.TryParse(value, ignoreCase: true, out scaling) && Enum.IsDefined(scaling) && !int.TryParse(value, out _);

    private static EngineCommand.Invalid TooManyVerbs() => new($"Use only one of --set, --stop, --list-monitors, --dump-desktop. {Usage}");
}
