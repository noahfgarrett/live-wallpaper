namespace LiveWallpaper.Core.Desktop;

public sealed record DesktopDump(string Plan, IReadOnlyList<string> Monitors, DesktopSnapshot Snapshot);
