namespace LiveWallpaper.Windows.Tests;

public sealed class SingleInstanceTests
{
    private static string UniqueName() => $@"Local\LiveWallpaper.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_WhileHeld_ReturnsNull()
    {
        WindowsOnly.Require();
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);

        using var second = SingleInstance.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterDispose_Succeeds()
    {
        WindowsOnly.Require();
        var name = UniqueName();
        SingleInstance.TryAcquire(name)?.Dispose();

        using var again = SingleInstance.TryAcquire(name);

        Assert.NotNull(again);
    }
}
