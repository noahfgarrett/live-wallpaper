namespace LiveWallpaper.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveWallpaper");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string LibraryDirectory { get; } = Path.Combine(DataDirectory, "library");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string RunningMarkerFile { get; } = Path.Combine(DataDirectory, "engine.running");
}
