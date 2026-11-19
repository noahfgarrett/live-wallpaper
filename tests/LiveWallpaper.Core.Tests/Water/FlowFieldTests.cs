using System.Drawing;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Water;

namespace LiveWallpaper.Core.Tests.Water;

public sealed class FlowFieldTests
{
    private static readonly Size Target = new(400, 400);
    private static readonly RectangleF Bounds = new(0, 0, 400, 400);

    private static Region Box(double l, double t, double r, double b, RegionPoint flow) =>
        new([new(l, t), new(r, t), new(r, b), new(l, b)]) { Flow = flow };

    private static (float X, float Y) At(FlowImage image, int targetX, int targetY)
    {
        var i = (((targetY / FlowField.Downscale) * image.Width) + (targetX / FlowField.Downscale)) * 2;
        return (image.Pixels[i], image.Pixels[i + 1]);
    }

    [Fact]
    public void FlowField_PaintsEachRegionsSpeed()
    {
        var set = new RegionSet([
            Box(0.0, 0.0, 0.4, 0.4, new RegionPoint(0, 0.12)),
            Box(0.6, 0.6, 1.0, 1.0, new RegionPoint(0.12, 0)),
        ]);

        var image = FlowField.Build(set, Bounds, Target, 0);

        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
        Assert.Equal((0f, 12f), At(image, 80, 80));
        var (x, y) = At(image, 320, 320);
        Assert.Equal(12f, x, 3);
        Assert.Equal(0f, y, 3);
    }

    [Fact]
    public void FlowField_LastRegionWins()
    {
        var set = new RegionSet([
            Box(0.0, 0.0, 0.6, 0.6, new RegionPoint(0, 0.12)),
            Box(0.4, 0.4, 1.0, 1.0, new RegionPoint(0, -0.12)),
        ]);

        var image = FlowField.Build(set, Bounds, Target, 0);

        Assert.Equal((0f, -12f), At(image, 240, 240));
        Assert.Equal((0f, 12f), At(image, 40, 40));
    }

    [Fact]
    public void FlowField_ZeroOutside()
    {
        var set = new RegionSet([Box(0.4, 0.4, 0.6, 0.6, new RegionPoint(0, 0.12))]);

        var image = FlowField.Build(set, Bounds, Target, 0);

        Assert.Equal((0f, 0f), At(image, 20, 20));
        Assert.Equal((0f, 0f), At(image, 380, 380));
    }

    [Fact]
    public void FlowField_EmptySet_AllZero()
    {
        var image = FlowField.Build(RegionSet.Empty, Bounds, Target, 50);

        Assert.All(image.Pixels, p => Assert.Equal(0f, p));
    }

    [Fact]
    public void FlowField_DilatesNearestRegionFlow_WithinReach()
    {
        var set = new RegionSet([Box(0.4, 0.4, 0.6, 0.6, new RegionPoint(0, 0.12))]);

        var image = FlowField.Build(set, Bounds, Target, 60);

        Assert.Equal((0f, 12f), At(image, 200, 230 + 40));
        Assert.Equal((0f, 12f), At(image, 200, 160 - 40));
    }

    [Fact]
    public void FlowField_BeyondReach_IsZero()
    {
        var set = new RegionSet([Box(0.4, 0.4, 0.6, 0.6, new RegionPoint(0, 0.12))]);

        var image = FlowField.Build(set, Bounds, Target, 60);

        Assert.Equal((0f, 0f), At(image, 200, 240 + 100));
    }

    [Fact]
    public void FlowField_DilationPicksTheNearerRegion()
    {
        var set = new RegionSet([
            Box(0.0, 0.4, 0.2, 0.6, new RegionPoint(0, 0.12)),
            Box(0.8, 0.4, 1.0, 0.6, new RegionPoint(0, -0.12)),
        ]);

        var image = FlowField.Build(set, Bounds, Target, 150);

        Assert.Equal((0f, 12f), At(image, 120, 200));
        Assert.Equal((0f, -12f), At(image, 280, 200));
    }

    public static TheoryData<string> Polygons => new() { "concave", "star" };

    [Theory]
    [MemberData(nameof(Polygons))]
    public void FlowField_ScanlineMatchesContainsPoint(string shape)
    {
        RegionPoint[] points = shape == "concave"
            ? [new(0.1, 0.1), new(0.5, 0.2), new(0.9, 0.1), new(0.7, 0.5), new(0.9, 0.9), new(0.4, 0.6), new(0.1, 0.8)]
            : Enumerable.Range(0, 64).Select(i =>
            {
                var angle = i * 2 * Math.PI / 64;
                var radius = i % 2 == 0 ? 0.45 : 0.18;
                return new RegionPoint(0.5 + (radius * Math.Cos(angle)), 0.5 + (radius * Math.Sin(angle)));
            }).ToArray();
        var set = new RegionSet([new Region(points) { Flow = new RegionPoint(0, 0.12) }]);

        var image = FlowField.Build(set, Bounds, Target, 0);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var centre = new RegionPoint((((x + 0.5) * FlowField.Downscale) - Bounds.X) / Bounds.Width, (((y + 0.5) * FlowField.Downscale) - Bounds.Y) / Bounds.Height);
                var expected = RegionGeometry.ContainsPoint(points, centre);
                Assert.Equal(expected, image.Pixels[((y * image.Width) + x) * 2 + 1] != 0);
            }
        }
    }
}
