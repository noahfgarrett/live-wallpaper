namespace LiveWallpaper.Core;

public sealed class UncleanShutdownMarker(string path)
{
    public bool WasUncleanShutdown() => File.Exists(path);

    public void MarkRunning()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void MarkStopped()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
