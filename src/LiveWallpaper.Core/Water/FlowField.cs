using System.Drawing;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Water;

/// <summary>The flow speed per pixel, red and green interleaved, in target pixels per second.</summary>
public sealed record FlowImage(int Width, int Height, float[] Pixels);

/// <summary>Rasterizes water regions into a quarter-resolution field of flow speeds.</summary>
public static class FlowField
{
    public const int Downscale = 4;

    private const float Straight = 1f;
    private const float Diagonal = 1.4142135f;

    /// <param name="water">The water regions; each carries its arrow.</param>
    /// <param name="imageBounds">Where the whole source image lands on the target, in pixels.</param>
    /// <param name="target">The render target size, in pixels.</param>
    /// <param name="dilatePx">
    /// Pixels outside every region but within this many target pixels of one take the nearest region's flow,
    /// so the mist plume knows its direction.
    /// </param>
    public static FlowImage Build(RegionSet water, RectangleF imageBounds, Size target, double dilatePx)
    {
        if (target.Width <= 0 || target.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Width and height must be positive.");
        }

        var width = (target.Width + Downscale - 1) / Downscale;
        var height = (target.Height + Downscale - 1) / Downscale;
        var pixels = new float[width * height * 2];
        var image = new FlowImage(width, height, pixels);
        if (water.Regions.Count == 0 || imageBounds.Width <= 0 || imageBounds.Height <= 0)
        {
            return image;
        }

        var distance = new float[width * height];
        Array.Fill(distance, float.PositiveInfinity);
        var crossings = new List<double>();
        foreach (var region in water.Regions)
        {
            Paint(region, imageBounds, width, height, pixels, distance, crossings);
        }

        var reachCells = (float)(dilatePx / Downscale);
        if (reachCells > 0)
        {
            Dilate(pixels, distance, width, height);
            for (var i = 0; i < distance.Length; i++)
            {
                if (distance[i] > reachCells)
                {
                    pixels[2 * i] = 0;
                    pixels[(2 * i) + 1] = 0;
                }
            }
        }
        return image;
    }

    private static void Paint(Region region, RectangleF bounds, int width, int height, float[] pixels, float[] distance, List<double> crossings)
    {
        if (region.Flow is not { } flow || region.Points.Count < 3)
        {
            return;
        }
        var (vx, vy) = WaterModel.SpeedPxPerSec(flow, bounds.Size);

        var minX = region.Points.Min(p => p.X);
        var maxX = region.Points.Max(p => p.X);
        var minY = region.Points.Min(p => p.Y);
        var maxY = region.Points.Max(p => p.Y);
        var left = Math.Max(0, (int)Math.Floor((((minX * bounds.Width) + bounds.X) / Downscale) - 1));
        var right = Math.Min(width - 1, (int)Math.Ceiling((((maxX * bounds.Width) + bounds.X) / Downscale) + 1));
        var top = Math.Max(0, (int)Math.Floor((((minY * bounds.Height) + bounds.Y) / Downscale) - 1));
        var bottom = Math.Min(height - 1, (int)Math.Ceiling((((maxY * bounds.Height) + bounds.Y) / Downscale) + 1));

        var points = region.Points;
        for (var y = top; y <= bottom; y++)
        {
            var ny = (((y + 0.5) * Downscale) - bounds.Y) / bounds.Height;
            crossings.Clear();
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                var a = points[i];
                var b = points[j];
                if ((a.Y > ny) != (b.Y > ny))
                {
                    crossings.Add(((b.X - a.X) * (ny - a.Y) / (b.Y - a.Y)) + a.X);
                }
            }
            if (crossings.Count == 0)
            {
                continue;
            }
            crossings.Sort();

            // Even-odd, matching RegionGeometry.ContainsPoint: a pixel is inside when an odd number of crossings lie at or left of it.
            var next = 0;
            for (var x = left; x <= right; x++)
            {
                var nx = (((x + 0.5) * Downscale) - bounds.X) / bounds.Width;
                while (next < crossings.Count && crossings[next] <= nx)
                {
                    next++;
                }
                if ((next & 1) == 0)
                {
                    continue;
                }
                var index = (y * width) + x;
                pixels[2 * index] = (float)vx;
                pixels[(2 * index) + 1] = (float)vy;
                distance[index] = 0;
            }
        }
    }

    /// <summary>Two-pass chamfer distance transform that carries the nearest region's flow along with the distance.</summary>
    private static void Dilate(float[] pixels, float[] distance, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Relax(pixels, distance, width, (y * width) + x, x - 1, y, Straight, width, height);
                Relax(pixels, distance, width, (y * width) + x, x - 1, y - 1, Diagonal, width, height);
                Relax(pixels, distance, width, (y * width) + x, x, y - 1, Straight, width, height);
                Relax(pixels, distance, width, (y * width) + x, x + 1, y - 1, Diagonal, width, height);
            }
        }
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                Relax(pixels, distance, width, (y * width) + x, x + 1, y, Straight, width, height);
                Relax(pixels, distance, width, (y * width) + x, x + 1, y + 1, Diagonal, width, height);
                Relax(pixels, distance, width, (y * width) + x, x, y + 1, Straight, width, height);
                Relax(pixels, distance, width, (y * width) + x, x - 1, y + 1, Diagonal, width, height);
            }
        }
    }

    private static void Relax(float[] pixels, float[] distance, int width, int index, int nx, int ny, float step, int w, int h)
    {
        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
        {
            return;
        }
        var neighbour = (ny * width) + nx;
        var candidate = distance[neighbour] + step;
        if (candidate < distance[index])
        {
            distance[index] = candidate;
            pixels[2 * index] = pixels[2 * neighbour];
            pixels[(2 * index) + 1] = pixels[(2 * neighbour) + 1];
        }
    }
}
