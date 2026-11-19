using System.Drawing;

namespace LiveWallpaper.Core.Regions;

/// <summary>A single-channel mask, row-major, one byte per pixel. 255 shows the effect fully, 0 hides it.</summary>
public sealed record MaskImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Rasterizes a region set into a mask covering the render target.
/// Each region is filled on its own (nonzero winding) and the fills are united, then blurred
/// with σ = featherPx / 2 (featherPx = featherPct / 100 × displayed image width), then inverted when asked.
/// </summary>
public static class RegionMask
{
    /// <summary>Wide blurs are computed on a smaller mask so σ is about this many mask pixels.</summary>
    public const double BlurSigmaAtMaskPx = 3;

    /// <summary>The smallest mask, as a fraction of the render target.</summary>
    public const double MinScale = 0.125;

    private const double MinFeatherPx = 0.5;
    private const int SubScanlines = 4;
    private const float SubScanlineWeight = 1f / SubScanlines;
    private const int BoxPasses = 3;

    public static double ScaleFor(double sigmaPx) =>
        sigmaPx <= BlurSigmaAtMaskPx ? 1 : Math.Max(MinScale, BlurSigmaAtMaskPx / sigmaPx);

    /// <param name="set">The regions to rasterize.</param>
    /// <param name="imageBounds">Where the whole source image lands on the target, in pixels.</param>
    /// <param name="target">The render target size, in pixels.</param>
    /// <returns>null when there are no regions, meaning the effect shows everywhere.</returns>
    public static MaskImage? Build(RegionSet set, RectangleF imageBounds, Size target)
    {
        if (target.Width <= 0 || target.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Width and height must be positive.");
        }
        if (set.Regions.Count == 0)
        {
            return null;
        }

        var featherPx = Math.Max(0, set.FeatherPct) / 100 * imageBounds.Width;
        var sigma = featherPx > MinFeatherPx ? featherPx / 2 : 0;
        var scale = ScaleFor(sigma);
        var width = Math.Max(1, (int)Math.Ceiling((target.Width * scale) - 1e-9));
        var height = Math.Max(1, (int)Math.Ceiling((target.Height * scale) - 1e-9));
        var sx = (double)width / target.Width;
        var sy = (double)height / target.Height;

        var pixels = new byte[width * height];
        var row = new float[width];
        var crossings = new List<Crossing>();
        foreach (var region in set.Regions)
        {
            var points = region.Points
                .Select(p => new RegionPoint((imageBounds.X + (p.X * imageBounds.Width)) * sx, (imageBounds.Y + (p.Y * imageBounds.Height)) * sy))
                .ToArray();
            Fill(points, pixels, width, height, row, crossings);
        }

        if (sigma > 0)
        {
            Blur(pixels, width, height, sigma * scale);
        }
        if (set.Invert)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(255 - pixels[i]);
            }
        }
        return new MaskImage(width, height, pixels);
    }

    private static void Fill(RegionPoint[] points, byte[] pixels, int width, int height, float[] row, List<Crossing> crossings)
    {
        var top = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
        var bottom = Math.Min(height - 1, (int)Math.Ceiling(points.Max(p => p.Y)) - 1);
        var left = Math.Clamp((int)Math.Floor(points.Min(p => p.X)), 0, width);
        var right = Math.Clamp((int)Math.Ceiling(points.Max(p => p.X)), 0, width);
        for (var y = top; y <= bottom; y++)
        {
            Array.Clear(row, left, right - left);
            for (var s = 0; s < SubScanlines; s++)
            {
                var scanY = y + ((s + 0.5) / SubScanlines);
                crossings.Clear();
                for (var i = 0; i < points.Length; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    if ((a.Y <= scanY) != (b.Y <= scanY))
                    {
                        var x = a.X + ((scanY - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                        crossings.Add(new Crossing(x, b.Y > a.Y ? 1 : -1));
                    }
                }
                crossings.Sort(static (p, q) => p.X.CompareTo(q.X));

                var winding = 0;
                var start = 0.0;
                foreach (var crossing in crossings)
                {
                    var before = winding;
                    winding += crossing.Direction;
                    if (before == 0 && winding != 0)
                    {
                        start = crossing.X;
                    }
                    else if (before != 0 && winding == 0)
                    {
                        AddSpan(row, start, crossing.X);
                    }
                }
            }

            var offset = y * width;
            for (var x = left; x < right; x++)
            {
                if (row[x] > 0)
                {
                    var value = (byte)Math.Round(Math.Min(1f, row[x]) * 255);
                    if (value > pixels[offset + x])
                    {
                        pixels[offset + x] = value;
                    }
                }
            }
        }
    }

    private static void AddSpan(float[] row, double from, double to)
    {
        var x0 = Math.Clamp(from, 0, row.Length);
        var x1 = Math.Clamp(to, 0, row.Length);
        if (x1 <= x0)
        {
            return;
        }
        var i0 = (int)Math.Floor(x0);
        var i1 = (int)Math.Floor(x1);
        if (i0 == i1)
        {
            row[i0] += (float)(x1 - x0) * SubScanlineWeight;
            return;
        }
        row[i0] += (float)(i0 + 1 - x0) * SubScanlineWeight;
        for (var i = i0 + 1; i < i1; i++)
        {
            row[i] += SubScanlineWeight;
        }
        if (i1 < row.Length)
        {
            row[i1] += (float)(x1 - i1) * SubScanlineWeight;
        }
    }

    /// <summary>Three box blurs per axis approximate a Gaussian. The frame border is extended (clamp to edge).</summary>
    private static void Blur(byte[] pixels, int width, int height, double sigma)
    {
        var temp = new byte[pixels.Length];
        foreach (var size in BoxSizes(sigma))
        {
            var radius = (size - 1) / 2;
            if (radius == 0)
            {
                continue;
            }
            BoxPass(pixels, temp, width, height, radius, horizontal: true);
            BoxPass(temp, pixels, width, height, radius, horizontal: false);
        }
    }

    private static int[] BoxSizes(double sigma)
    {
        var ideal = Math.Sqrt((12 * sigma * sigma / BoxPasses) + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0)
        {
            lower--;
        }
        var upper = lower + 2;
        var lowerCount = (int)Math.Round(((12 * sigma * sigma) - (BoxPasses * lower * lower) - (4 * BoxPasses * lower) - (3 * BoxPasses)) / ((-4.0 * lower) - 4));
        lowerCount = Math.Clamp(lowerCount, 0, BoxPasses);
        var sizes = new int[BoxPasses];
        for (var i = 0; i < BoxPasses; i++)
        {
            sizes[i] = i < lowerCount ? lower : upper;
        }
        return sizes;
    }

    private static void BoxPass(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var step = horizontal ? 1 : width;
        var window = (2 * radius) + 1;
        for (var line = 0; line < lines; line++)
        {
            var origin = horizontal ? line * width : line;
            var sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                sum += source[origin + (Math.Clamp(k, 0, length - 1) * step)];
            }
            for (var i = 0; i < length; i++)
            {
                destination[origin + (i * step)] = (byte)((sum + (window / 2)) / window);
                sum += source[origin + (Math.Min(i + radius + 1, length - 1) * step)];
                sum -= source[origin + (Math.Max(i - radius, 0) * step)];
            }
        }
    }

    private readonly record struct Crossing(double X, int Direction);
}
