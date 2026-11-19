using System.Diagnostics.CodeAnalysis;

namespace LiveWallpaper.Core.Models;

public sealed record MonitorAssignment(string MonitorId, string ImagePath, WallpaperScaling Scaling);

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [AllowNull]
    public IReadOnlyList<MonitorAssignment> Monitors
    {
        get;
        init => field = value ?? [];
    } = [];

}
