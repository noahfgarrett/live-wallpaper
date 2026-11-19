namespace LiveWallpaper.Core.Models;

public sealed record LibraryItem(
    Guid Id,
    string DisplayName,
    string FileName,
    DateTimeOffset ImportedAt,
    int PixelWidth,
    int PixelHeight,
    SnowSettings Snow);

public sealed record LibraryDocument(int SchemaVersion, IReadOnlyList<LibraryItem> Items)
{
    public const int CurrentSchemaVersion = 1;
}
