using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Settings;

namespace LiveWallpaper.Core.Library;

public sealed class LibraryStore(string libraryDirectory, TimeProvider timeProvider)
{
    private const string SourceBaseName = "source";
    private const string ThumbnailFileName = "thumb.jpg";
    private static readonly ConcurrentDictionary<string, Lock> s_gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = s_gates.GetOrAdd(Path.GetFullPath(libraryDirectory), _ => new Lock());

    private string IndexPath => Path.Combine(libraryDirectory, "library.json");

    public IReadOnlyList<LibraryItem> Load()
    {
        lock (_gate)
        {
            return LoadUnlocked(backUpCorrupt: true);
        }
    }

    public IReadOnlyList<LibraryItem> LoadReadOnly()
    {
        lock (_gate)
        {
            return LoadUnlocked(backUpCorrupt: false);
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
        var destination = Path.Combine(folder, fileName);
        File.Copy(sourcePath, destination);
        File.SetAttributes(destination, FileAttributes.Normal);

        var item = new LibraryItem(id, Path.GetFileNameWithoutExtension(sourcePath), fileName, timeProvider.GetUtcNow(), pixelWidth, pixelHeight, new SnowSettings());
        lock (_gate)
        {
            Save([.. LoadUnlocked(backUpCorrupt: true), item]);
        }
        return item;
    }

    public void Update(LibraryItem item)
    {
        lock (_gate)
        {
            var items = LoadUnlocked(backUpCorrupt: true);
            if (items.All(i => i.Id != item.Id))
            {
                throw new InvalidOperationException("That image is no longer in the library.");
            }
            Save(items.Select(i => i.Id == item.Id ? item : i).ToList());
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            var items = LoadUnlocked(backUpCorrupt: true);
            if (items.All(i => i.Id != id))
            {
                return false;
            }
            Save(items.Where(i => i.Id != id).ToList());
        }
        DeleteFolder(ItemFolder(id));
        return true;
    }

    public string ImagePath(LibraryItem item) => Path.Combine(ItemFolder(item.Id), item.FileName);

    public string ThumbnailPath(LibraryItem item) => Path.Combine(ItemFolder(item.Id), ThumbnailFileName);

    private string ItemFolder(Guid id) => Path.Combine(libraryDirectory, id.ToString("N"));

    private IReadOnlyList<LibraryItem> LoadUnlocked(bool backUpCorrupt)
    {
        if (!File.Exists(IndexPath))
        {
            return [];
        }
        try
        {
            using var stream = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.LibraryDocument)
                ?? throw new JsonException("Library file contained null.");
            var repaired = Repair(document);
            Validate(repaired);
            return repaired.Items;
        }
        catch (JsonException)
        {
            if (backUpCorrupt)
            {
                var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                File.Move(IndexPath, $"{IndexPath}.corrupt-{stamp}", overwrite: true);
            }
            return [];
        }
    }

    private void Save(IReadOnlyList<LibraryItem> items)
    {
        Directory.CreateDirectory(libraryDirectory);
        var tempPath = IndexPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, new LibraryDocument(LibraryDocument.CurrentSchemaVersion, items), CoreJsonContext.Default.LibraryDocument);
        }
        AtomicFile.Replace(tempPath, IndexPath);
    }

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("library", "event=delete-folder-failed", ex);
        }
    }

    private static LibraryDocument Repair(LibraryDocument document)
    {
        if (document.Items is null)
        {
            return document;
        }
        return document with { Items = document.Items.Select(RepairItem).ToList() };
    }

    private static LibraryItem RepairItem(LibraryItem item)
    {
        if (item?.Glass is null || item.Water is null)
        {
            return item!;
        }
        return item with
        {
            Glass = item.Glass with { Regions = EffectRules.Repair(EffectKind.Glass, item.Glass.Regions) },
            Water = item.Water with { Regions = EffectRules.Repair(EffectKind.Water, item.Water.Regions) },
        };
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
                || !item.Snow.IsValid
                || item.Regions is null
                || !item.Regions.IsValid
                || item.Rain?.Settings is null
                || !item.Rain.Settings.IsValid
                || !EffectRules.IsValid(EffectKind.Rain, item.Rain.Regions)
                || item.Glass?.Settings is null
                || !item.Glass.Settings.IsValid
                || !EffectRules.IsValid(EffectKind.Glass, item.Glass.Regions)
                || item.Water?.Settings is null
                || !item.Water.Settings.IsValid
                || !EffectRules.IsValid(EffectKind.Water, item.Water.Regions))
            {
                throw new JsonException("The library contains an invalid item.");
            }
        }
    }
}
