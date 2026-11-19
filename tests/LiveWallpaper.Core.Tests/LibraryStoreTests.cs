using LiveWallpaper.Core.Library;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 36, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();
    private readonly TempDirectory _source = new();
    private readonly LibraryStore _store;

    public LibraryStoreTests()
    {
        _store = new LibraryStore(Path.Combine(_dir.Path, "library"), new FixedTimeProvider(Now));
    }

    public void Dispose()
    {
        _dir.Dispose();
        _source.Dispose();
    }

    private string SourceFile(string name = "My Photo.png")
    {
        var path = _source.File(name);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    [Fact]
    public void Load_WhenEmpty_ReturnsNoItems() => Assert.Empty(_store.Load());

    [Fact]
    public void Import_CopiesFileIntoLibraryAndSaves()
    {
        var item = _store.Import(SourceFile(), 1920, 1080);

        Assert.True(File.Exists(_store.ImagePath(item)));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(_store.ImagePath(item)));
        Assert.EndsWith(".png", _store.ImagePath(item));
        Assert.Equal(item, Assert.Single(_store.Load()));
        Assert.Equal(Now, item.ImportedAt);
        Assert.Equal(new SnowSettings(), item.Snow);
    }

    [Fact]
    public void Import_UsesFileNameAsDisplayName() =>
        Assert.Equal("My Photo", _store.Import(SourceFile(), 10, 10).DisplayName);

    [Fact]
    public void Import_MissingSource_Throws() =>
        Assert.Throws<FileNotFoundException>(() => _store.Import(_source.File("nope.png"), 10, 10));

    [Fact]
    public void Remove_KeepsOriginalFile()
    {
        var source = SourceFile();
        var item = _store.Import(source, 10, 10);

        Assert.True(_store.Remove(item.Id));

        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Remove_DeletesLibraryCopy()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        var copy = _store.ImagePath(item);

        _store.Remove(item.Id);

        Assert.False(File.Exists(copy));
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalse() => Assert.False(_store.Remove(Guid.NewGuid()));

    [Fact]
    public void Update_PersistsSnowSettings()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        var snow = new SnowSettings(DensityPct: 80, WindPct: 5, MotionBlur: true);

        _store.Update(item with { Snow = snow });

        Assert.Equal(snow, Assert.Single(_store.Load()).Snow);
    }

    [Fact]
    public void Update_WithRegions_RoundTrips()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        var updated = item with { Regions = new RegionSet([RegionGeometry.CreateQuad()], Invert: true, FeatherPct: 2.5) };

        _store.Update(updated);

        Assert.Equal(updated, Assert.Single(_store.Load()));
    }

    [Fact]
    public void Load_LibraryFromM3WithoutRegions_LoadsWithEmptyRegions()
    {
        var folder = Path.Combine(_dir.Path, "library");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "library.json"), """
            {
                "schemaVersion": 1,
                "items": [
                    {
                        "id": "6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b",
                        "displayName": "Cabin",
                        "fileName": "source.jpg",
                        "importedAt": "2026-11-19T08:36:00+00:00",
                        "pixelWidth": 4000,
                        "pixelHeight": 3000,
                        "snow": {
                            "enabled": true,
                            "densityPct": 50,
                            "speedPct": 50,
                            "windPct": 30,
                            "sizePct": 50,
                            "motionBlur": false
                        }
                    }
                ]
            }
            """);

        var item = Assert.Single(_store.Load());

        Assert.Equal(RegionSet.Empty, item.Regions);
        Assert.Empty(Directory.GetFiles(folder, "library.json.corrupt-*"));
    }

    [Fact]
    public void Load_InvalidRegions_TreatedAsCorrupt()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        _store.Update(item with { Regions = new RegionSet([new Region([new(0.1, 0.1), new(0.2, 0.2)])]) });

        Assert.Empty(_store.Load());
        Assert.Single(Directory.GetFiles(Path.Combine(_dir.Path, "library"), "library.json.corrupt-*"));
    }

    [Fact]
    public void Load_CorruptLibrary_BacksUpAndReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "library"));
        File.WriteAllText(Path.Combine(_dir.Path, "library", "library.json"), "{ nope");

        Assert.Empty(_store.Load());
        Assert.Single(Directory.GetFiles(Path.Combine(_dir.Path, "library"), "library.json.corrupt-*"));
    }

    [Fact]
    public void Load_SnowPercentOutOfRange_TreatsAsCorrupt()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        var path = Path.Combine(_dir.Path, "library", "library.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"densityPct\": 50", "\"densityPct\": 500", StringComparison.Ordinal));

        Assert.Empty(_store.Load());
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Fact]
    public void ThumbnailPath_SitsNextToTheCopy()
    {
        var item = _store.Import(SourceFile(), 10, 10);

        Assert.Equal(Path.GetDirectoryName(_store.ImagePath(item)), Path.GetDirectoryName(_store.ThumbnailPath(item)));
        Assert.EndsWith("thumb.jpg", _store.ThumbnailPath(item));
    }

    [Fact]
    public async Task Import_ConcurrentImports_KeepEveryItem()
    {
        var sources = Enumerable.Range(0, 20).Select(i => SourceFile($"photo-{i}.png")).ToList();

        await Task.WhenAll(sources.Select(source => Task.Run(() => _store.Import(source, 10, 10), TestContext.Current.CancellationToken)));

        Assert.Equal(20, _store.Load().Count);
    }

    [Fact]
    public void Import_ReadOnlySource_CopyIsWritable()
    {
        var source = SourceFile();
        File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            var item = _store.Import(source, 10, 10);

            Assert.False(File.GetAttributes(_store.ImagePath(item)).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Remove_ReadOnlyCopy_StillDeletesTheFolder()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        File.SetAttributes(_store.ImagePath(item), FileAttributes.ReadOnly);

        Assert.True(_store.Remove(item.Id));

        Assert.False(Directory.Exists(Path.GetDirectoryName(_store.ImagePath(item))));
    }

    [Fact]
    public void LoadReadOnly_CorruptLibrary_ReturnsEmptyAndLeavesTheFile()
    {
        var index = Path.Combine(_dir.Path, "library", "library.json");
        Directory.CreateDirectory(Path.GetDirectoryName(index)!);
        File.WriteAllText(index, "{ nope");

        Assert.Empty(_store.LoadReadOnly());
        Assert.Equal("{ nope", File.ReadAllText(index));
    }

    [Fact]
    public async Task Update_WhileAnotherReaderBrieflyHasTheIndexOpen_Succeeds()
    {
        var item = _store.Import(SourceFile(), 10, 10);
        var index = Path.Combine(_dir.Path, "library", "library.json");
        var reader = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await reader.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        _store.Update(item with { Snow = new SnowSettings(DensityPct: 10) });
        await release;

        Assert.Equal(10, Assert.Single(_store.Load()).Snow.DensityPct);
    }

    [Fact]
    public void Load_WhileAnotherWriterReplacesTheIndex_DoesNotBlockIt()
    {
        var item = _store.Import(SourceFile(), 10, 10);

        _store.Update(item with { DisplayName = "Renamed" });

        Assert.Equal("Renamed", Assert.Single(_store.LoadReadOnly()).DisplayName);
    }

    private const string M4Item = """
        {
            "schemaVersion": 1,
            "items": [
                {
                    "id": "6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b",
                    "displayName": "Cabin",
                    "fileName": "source.jpg",
                    "importedAt": "2026-11-19T08:36:00+00:00",
                    "pixelWidth": 4000,
                    "pixelHeight": 3000,
                    "snow": {
                        "enabled": true,
                        "densityPct": 50,
                        "speedPct": 50,
                        "windPct": 30,
                        "sizePct": 50,
                        "motionBlur": false
                    },
                    "regions": {
                        "regions": [
                            { "points": [ { "x": 0.1, "y": 0.1 }, { "x": 0.5, "y": 0.1 }, { "x": 0.3, "y": 0.5 } ] }
                        ],
                        "invert": true,
                        "featherPct": 2
                    }
                }
            ]
        }
        """;

    private void WriteLibrary(string json)
    {
        var folder = Path.Combine(_dir.Path, "library");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "library.json"), json);
    }

    [Fact]
    public void Load_LibraryFromM4WithoutEffects_KeepsSnowRegionsAndTurnsNewEffectsOff()
    {
        WriteLibrary(M4Item);

        var item = Assert.Single(_store.Load());

        Assert.True(item.Snow.Enabled);
        Assert.Single(item.Regions.Regions);
        Assert.True(item.Regions.Invert);
        Assert.Equal(2, item.Regions.FeatherPct);
        Assert.Equal(RainSlot.Default, item.Rain);
        Assert.Equal(GlassSlot.Default, item.Glass);
        Assert.Equal(WaterSlot.Default, item.Water);
        Assert.False(item.Rain.Settings.Enabled);
        Assert.False(item.Glass.Settings.Enabled);
        Assert.False(item.Water.Settings.Enabled);
    }

    [Fact]
    public void RoundTrip_AllSlots()
    {
        var item = _store.Import(SourceFile(), 1920, 1080) with
        {
            Rain = new RainSlot(new RainSettings(true, 10, 20, 30, 40, 60), new RegionSet([new Region([new(0.1, 0.1), new(0.5, 0.1), new(0.3, 0.5)])], true, 3)),
            Glass = new GlassSlot(new GlassSettings(true, 11, 22, 33, 44), new RegionSet([RegionGeometry.CreateQuad()], false, 2)),
            Water = new WaterSlot(new WaterSettings(true, 70, 80), new RegionSet([new Region([new(0.1, 0.6), new(0.9, 0.6), new(0.9, 0.9)]) { Flow = new RegionPoint(0.25, -0.1) }])),
        };
        _store.Update(item);

        var loaded = Assert.Single(_store.Load());

        Assert.Equal(item.Rain, loaded.Rain);
        Assert.Equal(item.Glass, loaded.Glass);
        Assert.Equal(item.Water, loaded.Water);
        Assert.Equal(new RegionPoint(0.25, -0.1), loaded.Water.Regions.Regions[0].Flow);
    }

    [Fact]
    public void WaterSettings_MissingMist_LoadsAs40()
    {
        WriteLibrary(M4Item.Replace(
            "\"regions\": {",
            "\"water\": { \"settings\": { \"enabled\": true, \"stretchPct\": 50, \"foamPct\": 30 }, \"regions\": { \"regions\": [], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {"));

        var item = Assert.Single(_store.Load());

        Assert.Equal(40, item.Water.Settings.MistPct);
    }

    [Fact]
    public void Load_WaterRegionWithoutFlow_GetsDefaultArrow()
    {
        WriteLibrary(M4Item.Replace(
            "\"regions\": {",
            "\"water\": { \"settings\": { \"enabled\": true, \"stretchPct\": 50, \"foamPct\": 30 }, \"regions\": { \"regions\": [ { \"points\": [ { \"x\": 0.1, \"y\": 0.1 }, { \"x\": 0.5, \"y\": 0.1 }, { \"x\": 0.3, \"y\": 0.5 } ] } ], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {"));

        var item = Assert.Single(_store.Load());

        Assert.Equal(EffectRules.DefaultFlow, Assert.Single(item.Water.Regions.Regions).Flow);
    }

    [Fact]
    public void Load_GlassRegionWithFivePoints_IsDropped()
    {
        WriteLibrary(M4Item.Replace(
            "\"regions\": {",
            "\"glass\": { \"settings\": { \"enabled\": true, \"dropsPct\": 50, \"dropSizePct\": 50, \"slidingPct\": 40, \"fogPct\": 30 }, \"regions\": { \"regions\": [ { \"points\": [ { \"x\": 0.1, \"y\": 0.1 }, { \"x\": 0.5, \"y\": 0.1 }, { \"x\": 0.6, \"y\": 0.3 }, { \"x\": 0.5, \"y\": 0.5 }, { \"x\": 0.1, \"y\": 0.5 } ] } ], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {"));

        var item = Assert.Single(_store.Load());

        Assert.Empty(item.Glass.Regions.Regions);
        Assert.True(item.Glass.Settings.Enabled);
    }

    [Fact]
    public void Load_NaNRainDensityString_FailsJsonParsingAndIsTreatedAsCorrupt()
    {
        WriteLibrary(M4Item.Replace(
            "\"regions\": {",
            "\"rain\": { \"settings\": { \"enabled\": true, \"densityPct\": \"NaN\", \"speedPct\": 50, \"windPct\": 20, \"lengthPct\": 50, \"brightnessPct\": 50 }, \"regions\": { \"regions\": [], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {"));

        Assert.Empty(_store.Load());
    }

    private void AssertCorrupt(string insertedSlot)
    {
        WriteLibrary(M4Item.Replace("\"regions\": {", insertedSlot));

        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Load_RainDensityOver100_IsTreatedAsCorrupt() =>
        AssertCorrupt("\"rain\": { \"settings\": { \"enabled\": true, \"densityPct\": 150, \"speedPct\": 50, \"windPct\": 20, \"lengthPct\": 50, \"brightnessPct\": 50 }, \"regions\": { \"regions\": [], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {");

    [Fact]
    public void Load_GlassPointOutsideImage_IsTreatedAsCorrupt() =>
        AssertCorrupt("\"glass\": { \"settings\": { \"enabled\": true, \"dropsPct\": 50, \"dropSizePct\": 50, \"slidingPct\": 40, \"fogPct\": 30 }, \"regions\": { \"regions\": [ { \"points\": [ { \"x\": 0.1, \"y\": 0.1 }, { \"x\": 1.5, \"y\": 0.1 }, { \"x\": 0.5, \"y\": 0.5 }, { \"x\": 0.1, \"y\": 0.5 } ] } ], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {");

    [Fact]
    public void Load_GlassRegionWithFivePointsOutsideImage_IsTreatedAsCorrupt() =>
        AssertCorrupt("\"glass\": { \"settings\": { \"enabled\": true, \"dropsPct\": 50, \"dropSizePct\": 50, \"slidingPct\": 40, \"fogPct\": 30 }, \"regions\": { \"regions\": [ { \"points\": [ { \"x\": 0.1, \"y\": 0.1 }, { \"x\": 0.5, \"y\": 0.1 }, { \"x\": 0.6, \"y\": 0.3 }, { \"x\": 0.5, \"y\": 0.5 }, { \"x\": 1.5, \"y\": 0.5 } ] } ], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {");

    [Fact]
    public void Load_WaterFlowLongerThanOne_IsTreatedAsCorrupt() =>
        AssertCorrupt("\"water\": { \"settings\": { \"enabled\": true, \"stretchPct\": 50, \"foamPct\": 30 }, \"regions\": { \"regions\": [ { \"points\": [ { \"x\": 0.1, \"y\": 0.1 }, { \"x\": 0.5, \"y\": 0.1 }, { \"x\": 0.3, \"y\": 0.5 } ], \"flow\": { \"x\": 5, \"y\": 0 } } ], \"invert\": false, \"featherPct\": 1 } }, \"regions\": {");
}
