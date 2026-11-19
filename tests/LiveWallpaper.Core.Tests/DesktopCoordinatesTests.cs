using System.Drawing;
using LiveWallpaper.Core.Geometry;

namespace LiveWallpaper.Core.Tests;

public sealed class DesktopCoordinatesTests
{
    [Fact]
    public void ScreenToParentClient_MonitorLeftOfPrimary_StartsAtParentOrigin()
    {
        Assert.Equal(new Rectangle(0, 0, 1920, 1080),
            DesktopCoordinates.ScreenToParentClient(new Rectangle(-1920, 0, 1920, 1080), new Point(-1920, 0)));
    }

    [Fact]
    public void ScreenToParentClient_PrimaryAfterLeftMonitor_IsOffset()
    {
        Assert.Equal(new Rectangle(1920, 0, 2560, 1440),
            DesktopCoordinates.ScreenToParentClient(new Rectangle(0, 0, 2560, 1440), new Point(-1920, 0)));
    }

    [Fact]
    public void ScreenToParentClient_MonitorAbovePrimary_HandlesNegativeY()
    {
        Assert.Equal(new Rectangle(4480, 0, 1920, 1080),
            DesktopCoordinates.ScreenToParentClient(new Rectangle(2560, -300, 1920, 1080), new Point(-1920, -300)));
    }
}
