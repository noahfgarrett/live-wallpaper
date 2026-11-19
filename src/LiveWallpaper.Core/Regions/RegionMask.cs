using System.Buffers;
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
    /// <summary>
    /// Blurs wider than this many pixels are computed on a smaller mask, so σ is about this many mask pixels.
    /// The GPU's linear filtering adds a little more softness when it scales the mask up, and a full-resolution
    /// 4K blur would be too slow to rebuild while a corner is dragged live.
    /// </summary>
    public const double BlurSigmaAtMaskPx = 1;

    /// <summary>The smallest mask, as a fraction of the render target.</summary>
    public const double MinScale = 0.125;

    private const double MinFeatherPx = 0.5;
    private const int SubScanlines = 4;
    private const float SubScanlineWeight = 1f / SubScanlines;
    private const int BoxPasses = 3;
    private const int ReciprocalBits = 24;

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
        var cover = new float[width + 1];
        var delta = new float[width + 1];
        var crossings = new List<Crossing>();
        var filled = PixelRect.Empty;
        foreach (var region in set.Regions)
        {
            var points = region.Points
                .Select(p => new RegionPoint((imageBounds.X + (p.X * imageBounds.Width)) * sx, (imageBounds.Y + (p.Y * imageBounds.Height)) * sy))
                .ToArray();
            filled = filled.Union(Fill(points, pixels, width, height, cover, delta, crossings));
        }

        if (sigma > 0 && !filled.IsEmpty)
        {
            Blur(pixels, width, height, sigma * scale, filled);
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

    /// <summary>Fills one polygon into the mask (union by max) and returns the pixels it may have touched.</summary>
    private static PixelRect Fill(RegionPoint[] points, byte[] pixels, int width, int height, float[] cover, float[] delta, List<Crossing> crossings)
    {
        var top = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
        var bottom = Math.Min(height - 1, (int)Math.Ceiling(points.Max(p => p.Y)) - 1);
        var left = Math.Clamp((int)Math.Floor(points.Min(p => p.X)), 0, width);
        var right = Math.Clamp((int)Math.Ceiling(points.Max(p => p.X)), 0, width);
        if (top > bottom || left >= right)
        {
            return PixelRect.Empty;
        }
        for (var y = top; y <= bottom; y++)
        {
            Array.Clear(cover, left, right - left + 1);
            Array.Clear(delta, left, right - left + 1);
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
                        AddSpan(cover, delta, width, start, crossing.X);
                    }
                }
            }

            var offset = y * width;
            var running = 0f;
            for (var x = left; x < right; x++)
            {
                running += delta[x];
                var coverage = running + cover[x];
                if (coverage > 0)
                {
                    var value = (byte)Math.Round(Math.Min(1f, coverage) * 255);
                    if (value > pixels[offset + x])
                    {
                        pixels[offset + x] = value;
                    }
                }
            }
        }
        return new PixelRect(left, top, right - 1, bottom);
    }

    /// <summary>
    /// Adds one sub-scanline span: partial end pixels go straight into <paramref name="cover"/>, and the whole pixels
    /// between them into <paramref name="delta"/>, which the caller turns into coverage with a running sum.
    /// </summary>
    private static void AddSpan(float[] cover, float[] delta, int width, double from, double to)
    {
        var x0 = Math.Clamp(from, 0, width);
        var x1 = Math.Clamp(to, 0, width);
        if (x1 <= x0)
        {
            return;
        }
        var i0 = (int)Math.Floor(x0);
        var i1 = (int)Math.Floor(x1);
        if (i0 == i1)
        {
            cover[i0] += (float)(x1 - x0) * SubScanlineWeight;
            return;
        }
        cover[i0] += (float)(i0 + 1 - x0) * SubScanlineWeight;
        delta[i0 + 1] += SubScanlineWeight;
        delta[i1] -= SubScanlineWeight;
        cover[i1] += (float)(x1 - i1) * SubScanlineWeight;
    }

    /// <summary>
    /// Three box blurs per axis approximate a Gaussian. The frame border is extended (clamp to edge).
    /// Only the filled area plus the blur's reach is processed: everything else is zero before and after.
    /// </summary>
    private static void Blur(byte[] pixels, int width, int height, double sigma, PixelRect filled)
    {
        var radii = BoxSizes(sigma).Select(size => (size - 1) / 2).Where(r => r > 0).ToArray();
        if (radii.Length == 0)
        {
            return;
        }
        var reach = radii.Sum() + 1;
        var area = filled.Grow(reach).ClampTo(width, height);
        var band = area.Grow(radii.Max()).ClampTo(width, height);

        var temp = ArrayPool<byte>.Shared.Rent(pixels.Length);
        var sums = ArrayPool<int>.Shared.Rent(width);
        try
        {
            for (var y = band.Top; y <= band.Bottom; y++)
            {
                Array.Clear(temp, (y * width) + band.Left, band.Width);
            }
            foreach (var radius in radii)
            {
                BoxHorizontal(pixels, temp, width, area, radius);
                BoxVertical(temp, pixels, width, height, area, radius, sums);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(sums);
            ArrayPool<byte>.Shared.Return(temp);
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

    /// <summary>Rounded division by the window, as a multiply and shift.</summary>
    private static long Reciprocal(int window) => ((1L << ReciprocalBits) + (window / 2)) / window;

    private static byte Average(long sum, long reciprocal) => (byte)Math.Min(255, ((sum * reciprocal) + (1L << (ReciprocalBits - 1))) >> ReciprocalBits);

    private static void BoxHorizontal(byte[] source, byte[] destination, int width, PixelRect area, int radius)
    {
        var reciprocal = Reciprocal((2 * radius) + 1);
        var last = width - 1;
        for (var y = area.Top; y <= area.Bottom; y++)
        {
            var row = y * width;
            var sum = 0;
            for (var k = area.Left - radius; k <= area.Left + radius; k++)
            {
                sum += source[row + Math.Clamp(k, 0, last)];
            }
            for (var x = area.Left; x <= area.Right; x++)
            {
                destination[row + x] = Average(sum, reciprocal);
                sum += source[row + Math.Min(x + radius + 1, last)] - source[row + Math.Max(x - radius, 0)];
            }
        }
    }

    /// <summary>Walks down the rows keeping one running sum per column, so memory is read in order.</summary>
    private static void BoxVertical(byte[] source, byte[] destination, int width, int height, PixelRect area, int radius, int[] sums)
    {
        var reciprocal = Reciprocal((2 * radius) + 1);
        var last = height - 1;
        Array.Clear(sums, area.Left, area.Width);
        for (var k = area.Top - radius; k <= area.Top + radius; k++)
        {
            var row = Math.Clamp(k, 0, last) * width;
            for (var x = area.Left; x <= area.Right; x++)
            {
                sums[x] += source[row + x];
            }
        }
        for (var y = area.Top; y <= area.Bottom; y++)
        {
            var row = y * width;
            var add = Math.Min(y + radius + 1, last) * width;
            var remove = Math.Max(y - radius, 0) * width;
            for (var x = area.Left; x <= area.Right; x++)
            {
                destination[row + x] = Average(sums[x], reciprocal);
                sums[x] += source[add + x] - source[remove + x];
            }
        }
    }

    /// <summary>An inclusive pixel rectangle.</summary>
    private readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public static PixelRect Empty { get; } = new(0, 0, -1, -1);

        public bool IsEmpty => Right < Left || Bottom < Top;

        public int Width => Right - Left + 1;

        public PixelRect Union(PixelRect other) =>
            IsEmpty ? other
            : other.IsEmpty ? this
            : new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

        public PixelRect Grow(int by) => new(Left - by, Top - by, Right + by, Bottom + by);

        public PixelRect ClampTo(int width, int height) =>
            new(Math.Max(0, Left), Math.Max(0, Top), Math.Min(width - 1, Right), Math.Min(height - 1, Bottom));
    }

    private readonly record struct Crossing(double X, int Direction);
}
