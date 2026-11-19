using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Models;

public static class EffectRules
{
    public const int GlassPanePoints = 4;
    public const double MinFlowLength = 0.002;
    public const double MaxFlowLength = 1;

    public static readonly RegionPoint DefaultFlow = new(0, 0.12);

    /// <summary>
    /// Makes a loaded region set obey the effect's shape rules: glass loses non-quads, water gets the default arrow where it has none.
    /// Snow and rain are returned unchanged. Call it on a set that already passed <see cref="RegionSet.IsValid"/>.
    /// </summary>
    public static RegionSet Repair(EffectKind kind, RegionSet set)
    {
        switch (kind)
        {
            case EffectKind.Snow:
            case EffectKind.Rain:
                return set;
            case EffectKind.Glass:
                return set with
                {
                    Invert = false,
                    Regions = set.Regions.Where(r => r.Points.Count == GlassPanePoints).ToArray(),
                };
            case EffectKind.Water:
                return set with
                {
                    Invert = false,
                    Regions = set.Regions.Select(WithFlow).ToArray(),
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public static bool IsValid(EffectKind kind, RegionSet set)
    {
        if (set is null || !set.IsValid)
        {
            return false;
        }
        switch (kind)
        {
            case EffectKind.Snow:
            case EffectKind.Rain:
                return true;
            case EffectKind.Glass:
                return !set.Invert && set.Regions.All(r => r.Points.Count == GlassPanePoints);
            case EffectKind.Water:
                return !set.Invert && set.Regions.All(r => IsValidFlow(r.Flow));
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static Region WithFlow(Region region) => region.Flow is null ? region with { Flow = DefaultFlow } : region;

    public static bool IsValidFlow(RegionPoint? flow)
    {
        if (flow is not { } f || !double.IsFinite(f.X) || !double.IsFinite(f.Y))
        {
            return false;
        }
        var length = RegionGeometry.Distance(new RegionPoint(0, 0), f);
        return length is >= MinFlowLength and <= MaxFlowLength;
    }
}
