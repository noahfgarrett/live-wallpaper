namespace LiveWallpaper.Core.Regions;

/// <summary>A 2D point. Regions store it in normalized image coordinates (0–1); the geometry helpers also use it for screen pixels.</summary>
public readonly record struct RegionPoint(double X, double Y);

/// <summary>An axis-aligned rectangle in normalized image coordinates.</summary>
public readonly record struct NormalizedRect(double Left, double Top, double Right, double Bottom)
{
    public static NormalizedRect Unit { get; } = new(0, 0, 1, 1);

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public RegionPoint Clamp(RegionPoint p) => new(Math.Clamp(p.X, Left, Right), Math.Clamp(p.Y, Top, Bottom));
}

/// <summary>A polygon in normalized image coordinates.</summary>
public sealed record Region(IReadOnlyList<RegionPoint> Points)
{
    /// <summary>Exactly four corners: a plane that future effects may map into with a homography.</summary>
    public bool IsPlane => Points.Count == 4;

    public bool Equals(Region? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        if (other is null || Points is null || other.Points is null)
        {
            return false;
        }
        return Points.SequenceEqual(other.Points);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in Points ?? [])
        {
            hash.Add(point);
        }
        return hash.ToHashCode();
    }
}

/// <summary>The regions of one effect, plus whether they are inverted and how soft their edges are.</summary>
public sealed record RegionSet(IReadOnlyList<Region> Regions, bool Invert = false, double FeatherPct = RegionSet.DefaultFeatherPct)
{
    public const int MaxRegions = 16;
    public const int MaxPoints = 64;
    public const double MaxFeatherPct = 10;
    public const double DefaultFeatherPct = 1;

    public static RegionSet Empty { get; } = new([]);

    public bool Equals(RegionSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        if (other is null || Invert != other.Invert || !FeatherPct.Equals(other.FeatherPct) || Regions is null || other.Regions is null)
        {
            return false;
        }
        return Regions.SequenceEqual(other.Regions);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Invert);
        hash.Add(FeatherPct);
        foreach (var region in Regions ?? [])
        {
            hash.Add(region);
        }
        return hash.ToHashCode();
    }
}
