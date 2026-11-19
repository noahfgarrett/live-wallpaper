namespace LiveWallpaper.Windows.Tests;

public sealed class SingleAppInstanceTests
{
    [Fact]
    public void TryAcquire_SecondReturnsNull()
    {
        WindowsOnly.Require();
        var name = $@"Local\LiveWallpaper.Tests.App.{Guid.NewGuid():N}";
        using var first = SingleAppInstance.TryAcquire(name);

        using var second = SingleAppInstance.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
