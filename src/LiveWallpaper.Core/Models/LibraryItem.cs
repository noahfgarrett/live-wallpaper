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

    private readonly RainSlot _rain = RainSlot.Default;
    private readonly GlassSlot _glass = GlassSlot.Default;
    private readonly WaterSlot _water = WaterSlot.Default;

    /// <summary>Rain, glass and water are not constructor parameters, so a library saved before they existed loads with them off.</summary>
    public RainSlot Rain
    {
        get => _rain;
        init => _rain = value ?? RainSlot.Default;
    }

    public GlassSlot Glass
    {
        get => _glass;
        init => _glass = value ?? GlassSlot.Default;
    }

    public WaterSlot Water
    {
        get => _water;
        init => _water = value ?? WaterSlot.Default;
    }
}

public sealed record LibraryDocument(int SchemaVersion, IReadOnlyList<LibraryItem> Items)
{
    public const int CurrentSchemaVersion = 1;
}
