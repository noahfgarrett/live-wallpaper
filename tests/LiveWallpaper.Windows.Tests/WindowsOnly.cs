namespace LiveWallpaper.Windows.Tests;

internal static class WindowsOnly
{
    public static void Require() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
}
