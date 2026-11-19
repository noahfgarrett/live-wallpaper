using System.Drawing;

namespace LiveWallpaper.Core.Regions;

/// <summary>
/// Maps between normalized image coordinates and screen pixels for one view of the image.
/// </summary>
/// <param name="ImageBounds">Where the whole source image lands, in pixels: <c>ScalingMath.ComputeDestination(fullImageSize, viewport, scaling)</c>. Under Center scaling this extends past the viewport.</param>
/// <param name="Viewport">The visible area, in pixels.</param>
/// <param name="Scale">Device pixels per device-independent pixel, for sizing handles.</param>
public sealed record RegionView(RectangleF ImageBounds, Size Viewport, double Scale)
{
    public RegionPoint ToScreen(RegionPoint image) =>
        new(ImageBounds.X + (image.X * ImageBounds.Width), ImageBounds.Y + (image.Y * ImageBounds.Height));

    public RegionPoint ToImage(RegionPoint screen) =>
        new((screen.X - ImageBounds.X) / ImageBounds.Width, (screen.Y - ImageBounds.Y) / ImageBounds.Height);

    /// <summary>The part of the image that is on screen, in image coordinates.</summary>
    public NormalizedRect VisibleBounds
    {
        get
        {
            var topLeft = ToImage(new RegionPoint(0, 0));
            var bottomRight = ToImage(new RegionPoint(Viewport.Width, Viewport.Height));
            return new NormalizedRect(
                Math.Clamp(topLeft.X, 0, 1),
                Math.Clamp(topLeft.Y, 0, 1),
                Math.Clamp(bottomRight.X, 0, 1),
                Math.Clamp(bottomRight.Y, 0, 1));
        }
    }

    /// <summary>Moves a screen point onto the visible part of the image.</summary>
    public RegionPoint ClampToVisible(RegionPoint screen) => ToScreen(VisibleBounds.Clamp(ToImage(screen)));
}
