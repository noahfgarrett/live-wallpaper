namespace LiveWallpaper.Core.Models;

public static class MonitorAssignments
{
    public const string AllMonitors = "all";

    public static IReadOnlyList<MonitorAssignment>? Apply(
        IReadOnlyList<MonitorAssignment> current,
        IReadOnlyList<string> connectedMonitorIds,
        Guid libraryItemId,
        string monitorSelector,
        WallpaperScaling scaling)
    {
        if (string.Equals(monitorSelector, AllMonitors, StringComparison.OrdinalIgnoreCase))
        {
            return connectedMonitorIds.Select(id => new MonitorAssignment(id, libraryItemId, scaling)).ToList();
        }

        var monitorId = connectedMonitorIds.FirstOrDefault(id => string.Equals(id, monitorSelector, StringComparison.OrdinalIgnoreCase));
        if (monitorId is null)
        {
            return null;
        }
        return current
            .Where(a => !string.Equals(a.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase))
            .Append(new MonitorAssignment(monitorId, libraryItemId, scaling))
            .ToList();
    }

    public static IReadOnlyList<MonitorAssignment> RemoveItem(IReadOnlyList<MonitorAssignment> current, Guid libraryItemId) =>
        current.Where(a => a.LibraryItemId != libraryItemId).ToList();
}
