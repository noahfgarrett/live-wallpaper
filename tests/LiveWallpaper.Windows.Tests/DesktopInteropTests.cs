using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows.Tests;

public sealed class DesktopInteropTests
{
    [Fact]
    public void IsSlideshowActive_ReturnsWithoutCrashing()
    {
        WindowsOnly.Require();

        var ex = Record.Exception(() => DesktopWallpaperCom.IsSlideshowActive());

        Assert.Null(ex);
    }

    [Fact]
    public void TopLevel_IncludesACreatedWindow()
    {
        WindowsOnly.Require();
        using var window = WallpaperWindow.Create();

        Assert.Contains(window.Handle, WindowEnumeration.TopLevel());
    }

    [Fact]
    public void DirectChildren_ReturnsOnlyImmediateChildren()
    {
        WindowsOnly.Require();
        using var parent = WallpaperWindow.Create();
        using var child = WallpaperWindow.Create();
        using var grandchild = WallpaperWindow.Create();
        MakeChild(child.Handle, parent.Handle);
        MakeChild(grandchild.Handle, child.Handle);

        Assert.Equal([child.Handle], WindowEnumeration.DirectChildren(parent.Handle));
    }

    private static void MakeChild(nint window, nint parent)
    {
        var style = (long)User32.GetWindowLongPtr(window, User32.GWL_STYLE);
        User32.SetWindowLongPtr(window, User32.GWL_STYLE, (nint)((style & ~User32.WS_POPUP) | User32.WS_CHILD));
        User32.SetParent(window, parent);
    }
}
