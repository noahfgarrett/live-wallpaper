using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Geometry;

/// <summary>A 3x3 projective transform, row-major. Maps the unit square onto a convex quad (Heckbert).</summary>
public readonly record struct Homography(
    double M11, double M12, double M13,
    double M21, double M22, double M23,
    double M31, double M32, double M33)
{
    private const double MinDenominator = 1e-9;
    private const double MinDeterminant = 1e-12;

    /// <summary>
    /// Maps (0,0), (1,0), (1,1), (0,1) onto tl, tr, br, bl.
    /// Returns null when the quad is degenerate, non-finite or not convex.
    /// </summary>
    public static Homography? SquareToQuad(RegionPoint tl, RegionPoint tr, RegionPoint br, RegionPoint bl)
    {
        var corners = new[] { tl, tr, br, bl };
        foreach (var c in corners)
        {
            if (!double.IsFinite(c.X) || !double.IsFinite(c.Y))
            {
                return null;
            }
        }
        if (!IsConvex(corners))
        {
            return null;
        }

        var dx1 = tr.X - br.X;
        var dx2 = bl.X - br.X;
        var dy1 = tr.Y - br.Y;
        var dy2 = bl.Y - br.Y;
        var sx = tl.X - tr.X + br.X - bl.X;
        var sy = tl.Y - tr.Y + br.Y - bl.Y;
        var den = (dx1 * dy2) - (dx2 * dy1);
        if (!double.IsFinite(den) || Math.Abs(den) < MinDenominator)
        {
            return null;
        }

        var g = ((sx * dy2) - (dx2 * sy)) / den;
        var h = ((dx1 * sy) - (sx * dy1)) / den;
        var result = new Homography(
            tr.X - tl.X + (g * tr.X), bl.X - tl.X + (h * bl.X), tl.X,
            tr.Y - tl.Y + (g * tr.Y), bl.Y - tl.Y + (h * bl.Y), tl.Y,
            g, h, 1);
        return result.IsFinite ? result : null;
    }

    public RegionPoint Map(RegionPoint p)
    {
        var w = (M31 * p.X) + (M32 * p.Y) + M33;
        return new RegionPoint(
            ((M11 * p.X) + (M12 * p.Y) + M13) / w,
            ((M21 * p.X) + (M22 * p.Y) + M23) / w);
    }

    /// <summary>The adjugate divided by the determinant, or null when the determinant is nearly zero.</summary>
    public Homography? Inverse()
    {
        var a = (M22 * M33) - (M23 * M32);
        var b = -((M21 * M33) - (M23 * M31));
        var c = (M21 * M32) - (M22 * M31);
        var det = (M11 * a) + (M12 * b) + (M13 * c);
        if (!double.IsFinite(det) || Math.Abs(det) < MinDeterminant)
        {
            return null;
        }

        var result = new Homography(
            a / det, -((M12 * M33) - (M13 * M32)) / det, ((M12 * M23) - (M13 * M22)) / det,
            b / det, ((M11 * M33) - (M13 * M31)) / det, -((M11 * M23) - (M13 * M21)) / det,
            c / det, -((M11 * M32) - (M12 * M31)) / det, ((M11 * M22) - (M12 * M21)) / det);
        return result.IsFinite ? result : null;
    }

    private bool IsFinite =>
        double.IsFinite(M11) && double.IsFinite(M12) && double.IsFinite(M13) &&
        double.IsFinite(M21) && double.IsFinite(M22) && double.IsFinite(M23) &&
        double.IsFinite(M31) && double.IsFinite(M32) && double.IsFinite(M33);

    private static bool IsConvex(RegionPoint[] corners)
    {
        var sign = 0;
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            var c = corners[(i + 2) % corners.Length];
            var cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            var s = Math.Sign(cross);
            if (s == 0 || (sign != 0 && s != sign))
            {
                return false;
            }
            sign = s;
        }
        return true;
    }
}
