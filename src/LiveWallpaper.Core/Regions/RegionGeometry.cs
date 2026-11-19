namespace LiveWallpaper.Core.Regions;

public readonly record struct EdgeHit(int EdgeIndex, double Distance, RegionPoint Closest);

/// <summary>Pure polygon operations, ported from the approved browser preview (prototype/regions.js).</summary>
public static class RegionGeometry
{
    public const int MinPoints = 3;

    private const double QuadStart = 0.35;
    private const double QuadEnd = 0.65;

    /// <summary>A quad over the middle of the image, corners in order TL, TR, BR, BL.</summary>
    public static Region CreateQuad() => CreateQuad(NormalizedRect.Unit);

    /// <summary>A quad over the middle 30% of <paramref name="within"/>, corners in order TL, TR, BR, BL.</summary>
    public static Region CreateQuad(NormalizedRect within)
    {
        var left = within.Left + (within.Width * QuadStart);
        var right = within.Left + (within.Width * QuadEnd);
        var top = within.Top + (within.Height * QuadStart);
        var bottom = within.Top + (within.Height * QuadEnd);
        return new Region([new(left, top), new(right, top), new(right, bottom), new(left, bottom)]);
    }

    public static Region MoveVertex(Region region, int index, RegionPoint p, NormalizedRect bounds)
    {
        var clamped = bounds.Clamp(p);
        return new Region(region.Points.Select((q, i) => i == index ? clamped : q).ToArray());
    }

    /// <summary>Adds a point after <c>Points[edgeIndex]</c>. Does nothing when the region already has the most points allowed.</summary>
    public static Region InsertVertex(Region region, int edgeIndex, RegionPoint p)
    {
        if (region.Points.Count >= RegionSet.MaxPoints)
        {
            return region;
        }
        var points = region.Points.ToList();
        points.Insert(edgeIndex + 1, NormalizedRect.Unit.Clamp(p));
        return new Region(points);
    }

    /// <summary>Removes a point, keeping at least <see cref="MinPoints"/>.</summary>
    public static Region RemoveVertex(Region region, int index)
    {
        if (region.Points.Count <= MinPoints)
        {
            return region;
        }
        return new Region(region.Points.Where((_, i) => i != index).ToArray());
    }

    /// <summary>
    /// Moves the whole region, stopping when any point would leave <paramref name="bounds"/>.
    /// A region already partly outside the bounds can move back in but never further out.
    /// </summary>
    public static Region Translate(Region region, double dx, double dy, NormalizedRect bounds)
    {
        var minX = region.Points.Min(p => p.X);
        var maxX = region.Points.Max(p => p.X);
        var minY = region.Points.Min(p => p.Y);
        var maxY = region.Points.Max(p => p.Y);
        var cdx = Math.Clamp(dx, Math.Min(0, bounds.Left - minX), Math.Max(0, bounds.Right - maxX));
        var cdy = Math.Clamp(dy, Math.Min(0, bounds.Top - minY), Math.Max(0, bounds.Bottom - maxY));
        if (cdx == 0 && cdy == 0)
        {
            return region;
        }
        return new Region(region.Points.Select(p => new RegionPoint(p.X + cdx, p.Y + cdy)).ToArray());
    }

    /// <summary>The closest edge within <paramref name="maxDistance"/> of <paramref name="p"/>, including the closing edge.</summary>
    public static EdgeHit? NearestEdge(IReadOnlyList<RegionPoint> points, RegionPoint p, double maxDistance)
    {
        EdgeHit? best = null;
        for (var i = 0; i < points.Count; i++)
        {
            var closest = ClosestOnSegment(p, points[i], points[(i + 1) % points.Count]);
            var distance = Distance(p, closest);
            if (distance <= maxDistance && (best is null || distance < best.Value.Distance))
            {
                best = new EdgeHit(i, distance, closest);
            }
        }
        return best;
    }

    /// <summary>Even-odd point-in-polygon test.</summary>
    public static bool ContainsPoint(IReadOnlyList<RegionPoint> points, RegionPoint p)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    public static double Distance(RegionPoint a, RegionPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static RegionPoint ClosestOnSegment(RegionPoint p, RegionPoint a, RegionPoint b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var lengthSquared = (vx * vx) + (vy * vy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - a.X) * vx) + ((p.Y - a.Y) * vy)) / lengthSquared, 0, 1);
        return new RegionPoint(a.X + (t * vx), a.Y + (t * vy));
    }
}
