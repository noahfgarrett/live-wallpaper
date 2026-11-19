using System.Globalization;
using System.Text.Json;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Settings;

public enum SettingsLoadOutcome
{
    Loaded,
    Missing,
    CorruptReplaced,
}

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadOutcome Outcome, string? BackupPath);

public sealed class SettingsStore(string filePath, TimeProvider timeProvider)
{
    public SettingsLoadResult Load()
    {
        if (!File.Exists(filePath))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.Missing, null);
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var settings = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.AppSettings)
                ?? throw new JsonException("Settings file contained null.");
            Validate(settings);
            return new SettingsLoadResult(settings, SettingsLoadOutcome.Loaded, null);
        }
        catch (JsonException)
        {
            var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var backupPath = $"{filePath}.corrupt-{stamp}";
            File.Move(filePath, backupPath, overwrite: true);
            var defaults = new AppSettings();
            Save(defaults);
            return new SettingsLoadResult(defaults, SettingsLoadOutcome.CorruptReplaced, backupPath);
        }
    }

    private static void Validate(AppSettings settings)
    {
        foreach (var assignment in settings.Monitors)
        {
            if (assignment is null
                || string.IsNullOrWhiteSpace(assignment.MonitorId)
                || string.IsNullOrWhiteSpace(assignment.ImagePath)
                || !Enum.IsDefined(assignment.Scaling))
            {
                throw new JsonException("Settings file contains an invalid monitor assignment.");
            }
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, settings, CoreJsonContext.Default.AppSettings);
        }
        File.Move(tempPath, filePath, overwrite: true);
    }
}
