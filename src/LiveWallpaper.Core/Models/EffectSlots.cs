using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Models;

public sealed record RainSlot(RainSettings Settings, RegionSet Regions)
{
    public static RainSlot Default { get; } = new(new RainSettings(), RegionSet.Empty);
}

public sealed record GlassSlot(GlassSettings Settings, RegionSet Regions)
{
    public static GlassSlot Default { get; } = new(new GlassSettings(), RegionSet.Empty);
}

public sealed record WaterSlot(WaterSettings Settings, RegionSet Regions)
{
    public static WaterSlot Default { get; } = new(new WaterSettings(), RegionSet.Empty);
}
