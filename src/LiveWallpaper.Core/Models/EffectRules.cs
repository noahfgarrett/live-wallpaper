using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Models;

public static class EffectRules
{
    public const int GlassPanePoints = 4;
    public const double MinFlowLength = 0.002;
    public const double MaxFlowLength = 1;

    /// <summary>How far inside the length bounds a clamped flow lands, so rounding can't push it back out.</summary>
    private const double ClampInset = 1e-9;

    public static readonly RegionPoint DefaultFlow = new(0, 0.12);

    /// <summary>
    /// Makes a loaded region set obey the effect's shape rules: glass loses non-quads, water gets the default arrow where it has none
    /// and a finite arrow of the wrong length is clamped (a non-finite one is left for <see cref="IsValid"/> to reject).
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

    private static Region WithFlow(Region region) => region.Flow switch
    {
        null => region with { Flow = DefaultFlow },
        { } f when double.IsFinite(f.X) && double.IsFinite(f.Y) => region with { Flow = ClampFlow(f) },
        _ => region,
    };

    /// <summary>Scales a flow into [MinFlowLength, MaxFlowLength], keeping its direction. A zero or non-finite flow becomes the default arrow.</summary>
    public static RegionPoint ClampFlow(RegionPoint flow)
    {
        var length = RegionGeometry.Distance(new RegionPoint(0, 0), flow);
        if (length <= 0 || !double.IsFinite(length))
        {
            return DefaultFlow;
        }
        if (length is >= MinFlowLength and <= MaxFlowLength)
        {
            return flow;
        }
        var target = Math.Clamp(length, MinFlowLength * (1 + ClampInset), MaxFlowLength * (1 - ClampInset));
        var factor = target / length;
        return new RegionPoint(flow.X * factor, flow.Y * factor);
    }

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
