using System.Drawing;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Water;

public static class WaterModel
{
    /// <summary>Seconds the water takes to cross one arrow length.</summary>
    public const double ArrowSeconds = 4;

    /// <summary>The shortest and longest arrow, in image heights after aspect correction.</summary>
    public const double MinArrow = 0.02;
    public const double MaxArrow = 0.5;

    /// <summary>The longest mist reach, as a fraction of the displayed image height.</summary>
    public const double MaxMistReachFraction = 0.14;

    public static double CycleSeconds(double stretchPct) => 0.5 + (2.5 * stretchPct / 100);

    public static double MistReachPx(double mistPct, double displayedImageHeightPx) =>
        (0.04 + (0.10 * mistPct / 100)) * displayedImageHeightPx;

    /// <summary>Limits the aspect-corrected arrow length to [MinArrow, MaxArrow]. A zero arrow points down.</summary>
    public static RegionPoint ClampArrow(RegionPoint flow, double imageAspect)
    {
        var length = Math.Sqrt((flow.X * imageAspect * flow.X * imageAspect) + (flow.Y * flow.Y));
        if (length <= 0 || !double.IsFinite(length))
        {
            return new RegionPoint(0, MinArrow);
        }
        var clamped = Math.Clamp(length, MinArrow, MaxArrow);
        var factor = clamped / length;
        return new RegionPoint(flow.X * factor, flow.Y * factor);
    }

    /// <summary>The flow speed in pixels per second, along the arrow's on-screen direction.</summary>
    public static (double X, double Y) SpeedPxPerSec(RegionPoint flow, SizeF displayedImagePx)
    {
        var w = displayedImagePx.Width;
        var h = displayedImagePx.Height;
        var dx = flow.X * w;
        var dy = flow.Y * h;
        var dirLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (dirLength <= 0 || h <= 0)
        {
            return (0, 0);
        }
        var aspectX = flow.X * w / h;
        var magnitude = Math.Sqrt((aspectX * aspectX) + (flow.Y * flow.Y)) / ArrowSeconds * h;
        return (dx / dirLength * magnitude, dy / dirLength * magnitude);
    }
}
