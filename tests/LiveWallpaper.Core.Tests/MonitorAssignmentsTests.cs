using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Tests;

public sealed class MonitorAssignmentsTests
{
    private static readonly Guid Old = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid New = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Monitors = ["MON-A", "MON-B", "MON-C"];

    [Fact]
    public void All_AssignsEveryConnectedMonitor()
    {
        var result = MonitorAssignments.Apply([], Monitors, New, "all", WallpaperScaling.Fit);

        Assert.NotNull(result);
        Assert.Equal(Monitors, result.Select(a => a.MonitorId));
        Assert.All(result, a => Assert.Equal((New, WallpaperScaling.Fit), (a.LibraryItemId, a.Scaling)));
    }

    [Fact]
    public void All_IsCaseInsensitive() =>
        Assert.NotNull(MonitorAssignments.Apply([], Monitors, New, "ALL", WallpaperScaling.Fill));

    [Fact]
    public void One_ReplacesOnlyThatMonitor()
    {
        IReadOnlyList<MonitorAssignment> current =
        [
            new("MON-A", Old, WallpaperScaling.Fill),
            new("MON-B", Old, WallpaperScaling.Fill),
        ];

        var result = MonitorAssignments.Apply(current, Monitors, New, "mon-b", WallpaperScaling.Center);

        Assert.NotNull(result);
        Assert.Equal(new MonitorAssignment("MON-A", Old, WallpaperScaling.Fill), result.Single(a => a.MonitorId == "MON-A"));
        Assert.Equal(new MonitorAssignment("MON-B", New, WallpaperScaling.Center), result.Single(a => a.MonitorId == "MON-B"));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void One_UnknownMonitor_ReturnsNull() =>
        Assert.Null(MonitorAssignments.Apply([], Monitors, New, "MON-Z", WallpaperScaling.Fill));

    [Fact]
    public void RemoveItem_DropsEveryAssignmentForIt()
    {
        IReadOnlyList<MonitorAssignment> current =
        [
            new("MON-A", Old, WallpaperScaling.Fill),
            new("MON-B", New, WallpaperScaling.Fill),
        ];

        Assert.Equal([new MonitorAssignment("MON-B", New, WallpaperScaling.Fill)], MonitorAssignments.RemoveItem(current, Old));
    }
}
