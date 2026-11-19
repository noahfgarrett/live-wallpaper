namespace LiveWallpaper.Core.Tests;

public sealed class UncleanShutdownMarkerTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void FreshMarker_IsNotUnclean() =>
        Assert.False(new UncleanShutdownMarker(_dir.File("engine.running")).WasUncleanShutdown());

    [Fact]
    public void MarkRunning_WithoutStop_IsUnclean()
    {
        var marker = new UncleanShutdownMarker(_dir.File("engine.running"));
        marker.MarkRunning();

        Assert.True(new UncleanShutdownMarker(_dir.File("engine.running")).WasUncleanShutdown());
    }

    [Fact]
    public void MarkStopped_ClearsUnclean()
    {
        var marker = new UncleanShutdownMarker(_dir.File("engine.running"));
        marker.MarkRunning();
        marker.MarkStopped();

        Assert.False(marker.WasUncleanShutdown());
    }

    [Fact]
    public void MarkRunning_CreatesMissingDirectory()
    {
        var marker = new UncleanShutdownMarker(Path.Combine(_dir.Path, "nested", "engine.running"));
        marker.MarkRunning();

        Assert.True(marker.WasUncleanShutdown());
    }
}
