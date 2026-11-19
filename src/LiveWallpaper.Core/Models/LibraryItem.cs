using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Models;

public sealed record LibraryItem(
    Guid Id,
    string DisplayName,
    string FileName,
    DateTimeOffset ImportedAt,
    int PixelWidth,
    int PixelHeight,
    SnowSettings Snow)
{
    private readonly RegionSet _regions = RegionSet.Empty;

    /// <summary>
    /// Where the snow shows. Not a constructor parameter, so a library saved before regions existed loads with none.
    /// The JSON source generator assigns <c>default</c> to an init property missing from the file, hence the null check.
    /// </summary>
    public RegionSet Regions
    {
        get => _regions;
        init => _regions = value ?? RegionSet.Empty;
    }
}

public sealed record LibraryDocument(int SchemaVersion, IReadOnlyList<LibraryItem> Items)
{
    public const int CurrentSchemaVersion = 1;
}
