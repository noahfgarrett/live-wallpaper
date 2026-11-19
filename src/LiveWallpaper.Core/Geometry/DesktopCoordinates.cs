using System.Drawing;

namespace LiveWallpaper.Core.Geometry;

public static class DesktopCoordinates
{
    public static Rectangle ScreenToParentClient(Rectangle monitorBounds, Point parentClientOriginOnScreen) =>
        new(
            monitorBounds.X - parentClientOriginOnScreen.X,
            monitorBounds.Y - parentClientOriginOnScreen.Y,
            monitorBounds.Width,
            monitorBounds.Height);
}
