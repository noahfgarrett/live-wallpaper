using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;

namespace LiveWallpaper.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 36, 0, TimeSpan.Zero);
    private static readonly Guid ItemA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly TempDirectory _dir = new();
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _store = new SettingsStore(_dir.File("settings.json"), new FixedTimeProvider(Now));
    }

    public void Dispose() => _dir.Dispose();

    private void WriteRaw(string json) => File.WriteAllText(_dir.File("settings.json"), json);

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultsWithMissingOutcome()
    {
        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Missing, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
        Assert.Equal(2, result.Settings.SchemaVersion);
        Assert.Equal(FrameRateMode.Fps60, result.Settings.FrameRate);
        Assert.True(result.Settings.KeepRunningWhenAppCloses);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAssignments()
    {
        var settings = new AppSettings
        {
            Monitors =
            [
                new MonitorAssignment("MON-A", ItemA, WallpaperScaling.Fill),
                new MonitorAssignment("MON-B", ItemB, WallpaperScaling.Fit),
            ],
        };

        _store.Save(settings);
        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(settings.Monitors, result.Settings.Monitors);
    }

    [Fact]
    public void Settings_RoundTripsFrameRateAndPreferences()
    {
        _store.Save(new AppSettings { FrameRate = FrameRateMode.Adaptive, DefaultScaling = WallpaperScaling.Center, KeepRunningWhenAppCloses = false });

        var loaded = _store.Load().Settings;

        Assert.Equal(FrameRateMode.Adaptive, loaded.FrameRate);
        Assert.Equal(WallpaperScaling.Center, loaded.DefaultScaling);
        Assert.False(loaded.KeepRunningWhenAppCloses);
        Assert.Contains("\"frameRate\": \"adaptive\"", File.ReadAllText(_dir.File("settings.json")));
    }

    [Fact]
    public void Save_WritesScalingAsLowercaseString()
    {
        _store.Save(new AppSettings { Monitors = [new MonitorAssignment("M", ItemA, WallpaperScaling.Center)] });

        Assert.Contains("\"scaling\": \"center\"", File.ReadAllText(_dir.File("settings.json")));
    }

    [Fact]
    public void Load_WhenJsonCorrupt_BacksUpAndReturnsDefaults()
    {
        WriteRaw("{ not json");

        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
        Assert.NotNull(result.BackupPath);
        Assert.EndsWith("settings.json.corrupt-20261119T083600Z", result.BackupPath);
        Assert.Equal("{ not json", File.ReadAllText(result.BackupPath));
    }

    [Fact]
    public void SettingsV1File_IsTreatedAsCorrupt()
    {
        WriteRaw("""{ "schemaVersion": 1, "monitors": [ { "monitorId": "M", "imagePath": "a.jpg", "scaling": "fill" } ] }""");

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ { "monitorId": "M", "libraryItemId": "11111111-1111-1111-1111-111111111111", "scaling": "zoom" } ] }""")]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ { "monitorId": "M", "libraryItemId": "11111111-1111-1111-1111-111111111111", "scaling": 7 } ] }""")]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ null ] }""")]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ { "monitorId": " ", "libraryItemId": "11111111-1111-1111-1111-111111111111", "scaling": "fill" } ] }""")]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ { "monitorId": "M", "libraryItemId": "00000000-0000-0000-0000-000000000000", "scaling": "fill" } ] }""")]
    [InlineData("""{ "schemaVersion": 2, "monitors": [ { "monitorId": "M", "scaling": "fill" } ] }""")]
    [InlineData("""{ "schemaVersion": 2, "frameRate": "fps144" }""")]
    public void Load_WhenSemanticallyInvalid_TreatsAsCorrupt(string json)
    {
        WriteRaw(json);

        Assert.Equal(SettingsLoadOutcome.CorruptReplaced, _store.Load().Outcome);
    }

    [Fact]
    public void Load_WhenMonitorsNull_ReturnsEmptyList()
    {
        WriteRaw("""{ "schemaVersion": 2, "monitors": null }""");

        var result = _store.Load();

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Empty(result.Settings.Monitors);
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
}
