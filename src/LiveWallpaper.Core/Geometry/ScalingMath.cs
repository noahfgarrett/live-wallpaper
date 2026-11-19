using System.Drawing;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Geometry;

public static class ScalingMath
{
    public const int MaxTextureDimension = 16384;

    private const double RoundingSlack = 1e-6;

    public static RectangleF ComputeDestination(Size image, Size target, WallpaperScaling scaling)
    {
        RequirePositive(image, nameof(image));
        RequirePositive(target, nameof(target));

        return scaling switch
        {
            WallpaperScaling.Stretch => new RectangleF(0, 0, target.Width, target.Height),
            WallpaperScaling.Center => Centered(image.Width, image.Height, target),
            WallpaperScaling.Fill => Scaled(image, target, Math.Max(WidthRatio(image, target), HeightRatio(image, target))),
            WallpaperScaling.Fit => Scaled(image, target, Math.Min(WidthRatio(image, target), HeightRatio(image, target))),
            _ => throw new ArgumentOutOfRangeException(nameof(scaling), scaling, "Unknown scaling mode."),
        };
    }

    /// <summary>
    /// Where the image lands on <paramref name="target"/> when it shows a scaled-down copy of <paramref name="reference"/>,
    /// such as the editor preview of a monitor. Differs from <see cref="ComputeDestination"/> only for Center, which
    /// depends on the real pixel size.
    /// </summary>
    public static RectangleF ComputeScaledDestination(Size image, Size reference, Size target, WallpaperScaling scaling)
    {
        RequirePositive(reference, nameof(reference));
        RequirePositive(target, nameof(target));

        var onReference = ComputeDestination(image, reference, scaling);
        var sx = (float)target.Width / reference.Width;
        var sy = (float)target.Height / reference.Height;
        return new RectangleF(onReference.X * sx, onReference.Y * sy, onReference.Width * sx, onReference.Height * sy);
    }

    public static Size ComputeDecodeSize(Size image, Size target, WallpaperScaling scaling)
    {
        RequirePositive(image, nameof(image));
        RequirePositive(target, nameof(target));

        double width;
        double height;
        switch (scaling)
        {
            case WallpaperScaling.Stretch:
                width = Math.Min(image.Width, target.Width);
                height = Math.Min(image.Height, target.Height);
                break;
            case WallpaperScaling.Center:
                width = Math.Min(image.Width, target.Width);
                height = Math.Min(image.Height, target.Height);
                break;
            case WallpaperScaling.Fill:
            case WallpaperScaling.Fit:
                var ratio = scaling == WallpaperScaling.Fill
                    ? Math.Max(WidthRatio(image, target), HeightRatio(image, target))
                    : Math.Min(WidthRatio(image, target), HeightRatio(image, target));
                var scale = Math.Min(1.0, ratio);
                width = image.Width * scale;
                height = image.Height * scale;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scaling), scaling, "Unknown scaling mode.");
        }

        var cap = Math.Min(1.0, MaxTextureDimension / Math.Max(width, height));
        return new Size(CeilToPixels(width * cap), CeilToPixels(height * cap));
    }

    public static Rectangle ComputeSourceCrop(Size image, Size target, WallpaperScaling scaling)
    {
        RequirePositive(image, nameof(image));
        RequirePositive(target, nameof(target));

        if (scaling != WallpaperScaling.Center)
        {
            return new Rectangle(Point.Empty, image);
        }
        var width = Math.Min(image.Width, target.Width);
        var height = Math.Min(image.Height, target.Height);
        return new Rectangle((image.Width - width) / 2, (image.Height - height) / 2, width, height);
    }

    private static double WidthRatio(Size image, Size target) => (double)target.Width / image.Width;

    private static double HeightRatio(Size image, Size target) => (double)target.Height / image.Height;

    private static RectangleF Scaled(Size image, Size target, double scale) =>
        Centered(image.Width * scale, image.Height * scale, target);

    private static RectangleF Centered(double width, double height, Size target) =>
        new((float)((target.Width - width) / 2), (float)((target.Height - height) / 2), (float)width, (float)height);

    private static int CeilToPixels(double value) => Math.Max(1, (int)Math.Ceiling(value - RoundingSlack));

    private static void RequirePositive(Size size, string name)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(name, size, "Width and height must be positive.");
        }
    }
}
