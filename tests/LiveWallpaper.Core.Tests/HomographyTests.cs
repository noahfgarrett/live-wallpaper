using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests;

public sealed class HomographyTests
{
    private static readonly RegionPoint Tl = new(100, 50);
    private static readonly RegionPoint Tr = new(700, 80);
    private static readonly RegionPoint Br = new(650, 500);
    private static readonly RegionPoint Bl = new(120, 420);

    [Fact]
    public void SquareToQuad_MapsCornersExactly()
    {
        var h = Homography.SquareToQuad(Tl, Tr, Br, Bl);

        Assert.NotNull(h);
        AssertNear(Tl, h.Value.Map(new RegionPoint(0, 0)));
        AssertNear(Tr, h.Value.Map(new RegionPoint(1, 0)));
        AssertNear(Br, h.Value.Map(new RegionPoint(1, 1)));
        AssertNear(Bl, h.Value.Map(new RegionPoint(0, 1)));
    }

    [Fact]
    public void Inverse_RoundTrips()
    {
        var h = Homography.SquareToQuad(Tl, Tr, Br, Bl)!.Value;
        var inv = h.Inverse();
        Assert.NotNull(inv);

        var random = new Random(7);
        for (var i = 0; i < 50; i++)
        {
            var p = new RegionPoint(random.NextDouble(), random.NextDouble());
            AssertNear(p, inv.Value.Map(h.Map(p)));
        }
    }

    [Fact]
    public void SquareToQuad_Collinear_IsNull() =>
        Assert.Null(Homography.SquareToQuad(new(0, 0), new(1, 1), new(2, 2), new(3, 3)));

    [Fact]
    public void SquareToQuad_BowTie_IsNull() =>
        Assert.Null(Homography.SquareToQuad(new(0, 0), new(10, 10), new(10, 0), new(0, 10)));

    [Fact]
    public void SquareToQuad_NonFinite_IsNull() =>
        Assert.Null(Homography.SquareToQuad(new(double.NaN, 0), Tr, Br, Bl));

    private static void AssertNear(RegionPoint expected, RegionPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-9);
        Assert.Equal(expected.Y, actual.Y, 1e-9);
    }
}
