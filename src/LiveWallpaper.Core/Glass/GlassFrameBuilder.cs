using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Glass;

[StructLayout(LayoutKind.Sequential)]
public struct GlassPane
{
    public Vector4 InvRow0;
    public Vector4 InvRow1;
    public Vector4 InvRow2;
    public Vector4 FwdRow0;
    public Vector4 FwdRow1;
    public Vector4 FwdRow2;
    /// <summary>x, y, width, height of the quad's bounding box in target pixels.</summary>
    public Vector4 BoundsPx;
    public Vector2 PanePx;
    public Vector2 Padding;
}

[StructLayout(LayoutKind.Sequential)]
public struct GlassFrameConstants
{
    public Vector2 Resolution;
    public float TimePhase;
    public float Scale;
    public float Drops;
    public float SizeMul;
    public float Sliding;
    public float Fog;
    public float PaneCount;
    public Vector3 Padding;
}

public static class GlassFrameBuilder
{
    /// <summary>
    /// Writes one pane per usable 4-point region, mapped to target pixels through imageBounds.
    /// With no regions, one pane covers the whole target. Returns the number of panes written.
    /// </summary>
    public static int BuildPanes(RegionSet panes, RectangleF imageBounds, Size target, Span<GlassPane> result)
    {
        if (panes.Regions.Count == 0)
        {
            if (result.Length == 0)
            {
                return 0;
            }
            var w = target.Width;
            var h = target.Height;
            return TryWrite(
                new RegionPoint(0, 0), new RegionPoint(w, 0), new RegionPoint(w, h), new RegionPoint(0, h),
                target, ref result[0]) ? 1 : 0;
        }

        var count = 0;
        foreach (var region in panes.Regions)
        {
            if (count >= result.Length)
            {
                break;
            }
            if (region.Points.Count != 4)
            {
                continue;
            }
            if (TryWrite(
                ToPixels(region.Points[0], imageBounds),
                ToPixels(region.Points[1], imageBounds),
                ToPixels(region.Points[2], imageBounds),
                ToPixels(region.Points[3], imageBounds),
                target, ref result[count]))
            {
                count++;
            }
        }
        return count;
    }

    public static void BuildFrame(double timeSec, GlassSettings settings, Size target, int paneCount, ref GlassFrameConstants result)
    {
        result.Resolution = new Vector2(target.Width, target.Height);
        result.TimePhase = (float)Fract(timeSec / SnowModel.PeriodSeconds);
        result.Scale = (float)(target.Height / (double)SnowModel.ReferenceHeightPx);
        result.Drops = (float)(settings.DropsPct / 100);
        result.SizeMul = (float)GlassModel.SizeMultiplier(settings.DropSizePct);
        result.Sliding = (float)(settings.SlidingPct / 100);
        result.Fog = (float)(settings.FogPct / 100);
        result.PaneCount = paneCount;
    }

    private static RegionPoint ToPixels(RegionPoint p, RectangleF bounds) =>
        new(bounds.X + (p.X * bounds.Width), bounds.Y + (p.Y * bounds.Height));

    private static bool TryWrite(RegionPoint tl, RegionPoint tr, RegionPoint br, RegionPoint bl, Size target, ref GlassPane pane)
    {
        if (SquareToQuadPair(tl, tr, br, bl) is not var (fwd, inv))
        {
            return false;
        }

        pane.FwdRow0 = Row(fwd.M11, fwd.M12, fwd.M13);
        pane.FwdRow1 = Row(fwd.M21, fwd.M22, fwd.M23);
        pane.FwdRow2 = Row(fwd.M31, fwd.M32, fwd.M33);
        pane.InvRow0 = Row(inv.M11, inv.M12, inv.M13);
        pane.InvRow1 = Row(inv.M21, inv.M22, inv.M23);
        pane.InvRow2 = Row(inv.M31, inv.M32, inv.M33);

        var minX = Math.Clamp(Math.Min(Math.Min(tl.X, tr.X), Math.Min(br.X, bl.X)), 0, target.Width);
        var maxX = Math.Clamp(Math.Max(Math.Max(tl.X, tr.X), Math.Max(br.X, bl.X)), 0, target.Width);
        var minY = Math.Clamp(Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(br.Y, bl.Y)), 0, target.Height);
        var maxY = Math.Clamp(Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(br.Y, bl.Y)), 0, target.Height);
        pane.BoundsPx = new Vector4((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
        pane.PanePx = new Vector2(
            (float)((Length(tl, tr) + Length(bl, br)) / 2),
            (float)((Length(tl, bl) + Length(tr, br)) / 2));
        pane.Padding = Vector2.Zero;
        return true;
    }

    private static (Homography Fwd, Homography Inv)? SquareToQuadPair(RegionPoint tl, RegionPoint tr, RegionPoint br, RegionPoint bl)
    {
        if (Homography.SquareToQuad(tl, tr, br, bl) is not { } fwd || fwd.Inverse() is not { } inv)
        {
            return null;
        }
        return (fwd, inv);
    }

    private static Vector4 Row(double a, double b, double c) => new((float)a, (float)b, (float)c, 0);

    private static double Length(RegionPoint a, RegionPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double Fract(double value)
    {
        var f = value - Math.Floor(value);
        return f >= 1 ? 0 : f;
    }
}
