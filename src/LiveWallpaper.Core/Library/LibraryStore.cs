using System.Globalization;
using System.Text.Json;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;

namespace LiveWallpaper.Core.Library;

public sealed class LibraryStore(string libraryDirectory, TimeProvider timeProvider)
{
    private const string SourceBaseName = "source";
    private const string ThumbnailFileName = "thumb.jpg";

    private string IndexPath => Path.Combine(libraryDirectory, "library.json");

    public IReadOnlyList<LibraryItem> Load()
    {
        if (!File.Exists(IndexPath))
        {
            return [];
        }
        try
        {
            using var stream = File.OpenRead(IndexPath);
            var document = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.LibraryDocument)
                ?? throw new JsonException("Library file contained null.");
            Validate(document);
            return document.Items;
        }
        catch (JsonException)
        {
            var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            File.Move(IndexPath, $"{IndexPath}.corrupt-{stamp}", overwrite: true);
            return [];
        }
    }

    public LibraryItem Import(string sourcePath, int pixelWidth, int pixelHeight)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The image to import was not found.", Path.GetFileName(sourcePath));
        }

        var id = Guid.NewGuid();
        var fileName = SourceBaseName + Path.GetExtension(sourcePath).ToLowerInvariant();
        var folder = ItemFolder(id);
        Directory.CreateDirectory(folder);
        File.Copy(sourcePath, Path.Combine(folder, fileName));

        var item = new LibraryItem(id, Path.GetFileNameWithoutExtension(sourcePath), fileName, timeProvider.GetUtcNow(), pixelWidth, pixelHeight, new SnowSettings());
        Save([.. Load(), item]);
        return item;
    }

    public void Update(LibraryItem item)
    {
        var items = Load();
        if (items.All(i => i.Id != item.Id))
        {
            throw new InvalidOperationException("That image is no longer in the library.");
        }
        Save(items.Select(i => i.Id == item.Id ? item : i).ToList());
    }

    public bool Remove(Guid id)
    {
        var items = Load();
        if (items.All(i => i.Id != id))
        {
            return false;
        }
        Save(items.Where(i => i.Id != id).ToList());
        var folder = ItemFolder(id);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        return true;
    }

    public string ImagePath(LibraryItem item) => Path.Combine(ItemFolder(item.Id), item.FileName);

    public string ThumbnailPath(LibraryItem item) => Path.Combine(ItemFolder(item.Id), ThumbnailFileName);

    private string ItemFolder(Guid id) => Path.Combine(libraryDirectory, id.ToString("N"));

    private void Save(IReadOnlyList<LibraryItem> items)
    {
        Directory.CreateDirectory(libraryDirectory);
        var tempPath = IndexPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, new LibraryDocument(LibraryDocument.CurrentSchemaVersion, items), CoreJsonContext.Default.LibraryDocument);
        }
        File.Move(tempPath, IndexPath, overwrite: true);
    }

    private static void Validate(LibraryDocument document)
    {
        if (document.SchemaVersion != LibraryDocument.CurrentSchemaVersion || document.Items is null)
        {
            throw new JsonException("Unsupported library version.");
        }
        foreach (var item in document.Items)
        {
            if (item is null
                || item.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(item.FileName)
                || item.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || item.FileName.Contains("..", StringComparison.Ordinal)
                || item.Snow is null
                || !item.Snow.IsValid)
            {
                throw new JsonException("The library contains an invalid item.");
            }
        }
    }
}
