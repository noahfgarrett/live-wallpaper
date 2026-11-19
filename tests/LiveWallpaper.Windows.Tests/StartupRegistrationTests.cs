using Microsoft.Win32;

namespace LiveWallpaper.Windows.Tests;

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = $@"Software\LiveWallpaper.Tests\{Guid.NewGuid():N}";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Enable_WritesQuotedPath()
    {
        WindowsOnly.Require();
        var registration = new StartupRegistration(_keyPath);

        registration.Enable(@"C:\Apps\Live Wallpaper\LiveWallpaper.Engine.exe");

        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        Assert.Equal("\"C:\\Apps\\Live Wallpaper\\LiveWallpaper.Engine.exe\"", key?.GetValue("LiveWallpaper"));
    }

    [Fact]
    public void Disable_RemovesValue()
    {
        WindowsOnly.Require();
        var registration = new StartupRegistration(_keyPath);
        registration.Enable(@"C:\a.exe");

        registration.Disable();

        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabled_ReflectsValue()
    {
        WindowsOnly.Require();
        var registration = new StartupRegistration(_keyPath);

        Assert.False(registration.IsEnabled());
        registration.Enable(@"C:\a.exe");
        Assert.True(registration.IsEnabled());
    }
}
