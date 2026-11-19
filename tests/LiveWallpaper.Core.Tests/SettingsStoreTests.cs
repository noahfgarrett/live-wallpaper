using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;

namespace LiveWallpaper.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 36, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _store = new SettingsStore(_dir.File("settings.json"), new FixedTimeProvider(Now));
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultsWithMissingOutcome()
    {
        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Missing, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
        Assert.Equal(1, result.Settings.SchemaVersion);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAssignments()
    {
        var settings = new AppSettings
        {
            Monitors =
            [
                new MonitorAssignment("MON-A", @"C:\Pictures\a.jpg", WallpaperScaling.Fill),
                new MonitorAssignment("MON-B", @"C:\Pictures\b.png", WallpaperScaling.Fit),
            ],
        };

        _store.Save(settings);
        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(settings.Monitors, result.Settings.Monitors);
    }

    [Fact]
    public void Save_WritesScalingAsLowercaseString()
    {
        _store.Save(new AppSettings { Monitors = [new MonitorAssignment("M", "a.jpg", WallpaperScaling.Center)] });

        Assert.Contains("\"scaling\": \"center\"", File.ReadAllText(_dir.File("settings.json")));
    }

    [Fact]
    public void Load_WhenJsonCorrupt_BacksUpAndReturnsDefaults()
    {
        File.WriteAllText(_dir.File("settings.json"), "{ not json");

        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
        Assert.NotNull(result.BackupPath);
        Assert.EndsWith("settings.json.corrupt-20261119T083600Z", result.BackupPath);
        Assert.Equal("{ not json", File.ReadAllText(result.BackupPath));
    }

    [Fact]
    public void Load_WhenScalingUnknown_TreatsAsCorrupt()
    {
        File.WriteAllText(_dir.File("settings.json"),
            """{ "schemaVersion": 1, "monitors": [ { "monitorId": "M", "imagePath": "a.jpg", "scaling": "zoom" } ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Fact]
    public void Load_WhenMonitorsNull_ReturnsEmptyList()
    {
        File.WriteAllText(_dir.File("settings.json"), """{ "schemaVersion": 1, "monitors": null }""");

        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
    }

    [Fact]
    public void Load_WhenAssignmentMissingImagePath_TreatsAsCorrupt()
    {
        File.WriteAllText(_dir.File("settings.json"),
            """{ "schemaVersion": 1, "monitors": [ { "monitorId": "M", "scaling": "fill" } ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        _store.Save(new AppSettings());

        Assert.Equal(["settings.json"], Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = new SettingsStore(Path.Combine(_dir.Path, "a", "b", "settings.json"), new FixedTimeProvider(Now));

        nested.Save(new AppSettings());

        Assert.True(File.Exists(Path.Combine(_dir.Path, "a", "b", "settings.json")));
    }

    [Fact]
    public void Load_WhenMonitorEntryNull_TreatsAsCorrupt()
    {
        File.WriteAllText(_dir.File("settings.json"), """{ "schemaVersion": 1, "monitors": [ null ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Fact]
    public void Load_WhenScalingNumeric_TreatsAsCorrupt()
    {
        File.WriteAllText(_dir.File("settings.json"),
            """{ "schemaVersion": 1, "monitors": [ { "monitorId": "M", "imagePath": "a.jpg", "scaling": 7 } ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Fact]
    public void Load_WhenMonitorIdEmpty_TreatsAsCorrupt()
    {
        File.WriteAllText(_dir.File("settings.json"),
            """{ "schemaVersion": 1, "monitors": [ { "monitorId": " ", "imagePath": "a.jpg", "scaling": "fill" } ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }
}
