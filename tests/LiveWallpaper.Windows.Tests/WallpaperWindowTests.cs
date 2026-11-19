using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows.Tests;

public sealed class WallpaperWindowTests
{
    [Fact]
    public void Dispose_AfterWindowDestroyedExternally_DoesNotThrow()
    {
        WindowsOnly.Require();
        var window = WallpaperWindow.Create();
        User32.DestroyWindow(window.Handle);

        var ex = Record.Exception(window.Dispose);

        Assert.Null(ex);
        Assert.False(User32.IsWindow(window.Handle));
    }

    [Fact]
    public void Create_WindowHasToolWindowAndNoActivateStyles()
    {
        WindowsOnly.Require();
        using var window = WallpaperWindow.Create();

        var exStyle = (long)User32.GetWindowLongPtr(window.Handle, User32.GWL_EXSTYLE);

        Assert.Equal(User32.WS_EX_TOOLWINDOW, exStyle & User32.WS_EX_TOOLWINDOW);
        Assert.Equal(User32.WS_EX_NOACTIVATE, exStyle & User32.WS_EX_NOACTIVATE);
    }

    [Fact]
    public void Create_WindowStartsHidden()
    {
        WindowsOnly.Require();
        using var window = WallpaperWindow.Create();

        Assert.False(User32.IsWindowVisible(window.Handle));
    }
}
