using LiveWallpaper.Core.Library;
using LiveWallpaper.Core.Models;

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
}
