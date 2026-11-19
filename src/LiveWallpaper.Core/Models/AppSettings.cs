using System.Diagnostics.CodeAnalysis;

namespace LiveWallpaper.Core.Models;

public sealed record MonitorAssignment(string MonitorId, Guid LibraryItemId, WallpaperScaling Scaling);

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [AllowNull]
    public IReadOnlyList<MonitorAssignment> Monitors
    {
        get;
        init => field = value ?? [];
    } = [];

    public FrameRateMode FrameRate { get; init; } = FrameRateMode.Fps60;

    public WallpaperScaling DefaultScaling { get; init; } = WallpaperScaling.Fill;

    public bool KeepRunningWhenAppCloses { get; init; } = true;
}
