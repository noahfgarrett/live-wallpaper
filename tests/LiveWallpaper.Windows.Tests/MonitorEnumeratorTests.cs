namespace LiveWallpaper.Windows.Tests;

public sealed class MonitorEnumeratorTests
{
    [Fact]
    public void Enumerate_ReturnsAtLeastOneMonitorWithNonEmptyBounds()
    {
        WindowsOnly.Require();

        var monitors = MonitorEnumerator.Enumerate();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.False(m.Bounds.IsEmpty));
        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
    }

    [Fact]
    public void Enumerate_ReportsExactlyOnePrimaryMonitor()
    {
        WindowsOnly.Require();

        Assert.Single(MonitorEnumerator.Enumerate(), m => m.IsPrimary);
    }
}
