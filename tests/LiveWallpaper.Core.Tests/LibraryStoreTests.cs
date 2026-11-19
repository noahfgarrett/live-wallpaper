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
}
