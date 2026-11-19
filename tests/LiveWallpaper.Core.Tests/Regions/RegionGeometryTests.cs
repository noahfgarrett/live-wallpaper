using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class RegionGeometryTests
{
    private static readonly RegionPoint[] Square = [new(100, 100), new(200, 100), new(200, 200), new(100, 200)];

    private static (double X, double Y)[] Rounded(Region region) =>
        region.Points.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).ToArray();

    [Fact]
    public void CreateQuad_MakesCentredFourCornerPlane()
    {
        var quad = RegionGeometry.CreateQuad();

        Assert.Equal([(0.35, 0.35), (0.65, 0.35), (0.65, 0.65), (0.35, 0.65)], Rounded(quad));
        Assert.True(quad.IsPlane);
    }

    [Fact]
    public void CreateQuad_WithinBounds_UsesMiddleOfVisiblePart()
    {
        var quad = RegionGeometry.CreateQuad(new NormalizedRect(0, 0.25, 1, 0.75));

        Assert.Equal([(0.35, 0.425), (0.65, 0.425), (0.65, 0.575), (0.35, 0.575)], Rounded(quad));
    }

    [Fact]
    public void InsertVertex_AddsPointAfterEdgeStart()
    {
        var region = RegionGeometry.InsertVertex(RegionGeometry.CreateQuad(), 0, new RegionPoint(0.5, 0.35));

        Assert.Equal(5, region.Points.Count);
        Assert.Equal(new RegionPoint(0.5, 0.35), region.Points[1]);
        Assert.False(region.IsPlane);
    }

    [Fact]
    public void InsertVertex_AtMaxPoints_IsNoOp()
    {
        var region = RegionGeometry.CreateQuad();
        while (region.Points.Count < RegionSet.MaxPoints)
        {
            region = RegionGeometry.InsertVertex(region, 0, new RegionPoint(0.5, 0.35));
        }

        Assert.Same(region, RegionGeometry.InsertVertex(region, 0, new RegionPoint(0.5, 0.35)));
    }

    [Fact]
    public void RemoveVertex_KeepsAtLeastThreePoints()
    {
        var triangle = RegionGeometry.RemoveVertex(RegionGeometry.CreateQuad(), 0);

        Assert.Equal(3, triangle.Points.Count);
        Assert.Equal(3, RegionGeometry.RemoveVertex(triangle, 0).Points.Count);
    }

    [Fact]
    public void MoveVertex_ClampsToImage()
    {
        var region = RegionGeometry.MoveVertex(RegionGeometry.CreateQuad(), 0, new RegionPoint(-0.2, 1.4), NormalizedRect.Unit);

        Assert.Equal(new RegionPoint(0, 1), region.Points[0]);
    }

    [Fact]
    public void MoveVertex_ClampsToGivenBounds()
    {
        var region = RegionGeometry.MoveVertex(RegionGeometry.CreateQuad(), 0, new RegionPoint(0.1, 0.9), new NormalizedRect(0.25, 0.2, 0.75, 0.8));

        Assert.Equal(new RegionPoint(0.25, 0.8), region.Points[0]);
    }

    [Fact]
    public void Translate_StopsAtImageEdgeAndKeepsShape()
    {
        var region = RegionGeometry.Translate(RegionGeometry.CreateQuad(), 0.5, 0, NormalizedRect.Unit);
        var xs = region.Points.Select(p => p.X).ToArray();

        Assert.Equal(1, xs.Max(), 9);
        Assert.Equal(0.3, xs.Max() - xs.Min(), 9);
    }

    [Fact]
    public void Translate_RegionAlreadyOutsideBounds_DoesNotSnapBack()
    {
        var region = new Region([new(0, 0.4), new(0.3, 0.4), new(0.3, 0.6), new(0, 0.6)]);
        var bounds = new NormalizedRect(0.2, 0, 1, 1);

        Assert.Equal(region, RegionGeometry.Translate(region, -0.1, 0, bounds));
        Assert.Equal(0.1, RegionGeometry.Translate(region, 0.1, 0, bounds).Points[0].X, 9);
    }

    [Fact]
    public void Regions_AreImmutable()
    {
        var quad = RegionGeometry.CreateQuad();

        RegionGeometry.MoveVertex(quad, 0, new RegionPoint(0, 0), NormalizedRect.Unit);

        Assert.Equal((0.35, 0.35), Rounded(quad)[0]);
    }

    [Fact]
    public void NearestEdge_FindsEdgeWithinReach()
    {
        var hit = RegionGeometry.NearestEdge(Square, new RegionPoint(150, 98), 6);

        Assert.NotNull(hit);
        Assert.Equal(0, hit.Value.EdgeIndex);
        Assert.Equal(2, hit.Value.Distance, 9);
        Assert.Equal(new RegionPoint(150, 100), hit.Value.Closest);
    }

    [Fact]
    public void NearestEdge_ReturnsNullOutOfReach() =>
        Assert.Null(RegionGeometry.NearestEdge(Square, new RegionPoint(150, 98), 1));

    [Fact]
    public void NearestEdge_IncludesClosingEdge() =>
        Assert.Equal(3, RegionGeometry.NearestEdge(Square, new RegionPoint(97, 150), 6)?.EdgeIndex);

    [Fact]
    public void ContainsPoint_InsideAndOutside()
    {
        Assert.True(RegionGeometry.ContainsPoint(Square, new RegionPoint(150, 150)));
        Assert.False(RegionGeometry.ContainsPoint(Square, new RegionPoint(250, 150)));
    }

    [Fact]
    public void RegionSet_Equality_ComparesPoints()
    {
        var a = new RegionSet([RegionGeometry.CreateQuad()], Invert: true, FeatherPct: 2);
        var b = new RegionSet([RegionGeometry.CreateQuad()], Invert: true, FeatherPct: 2);
        var moved = a with { Regions = [RegionGeometry.MoveVertex(a.Regions[0], 0, new RegionPoint(0.1, 0.1), NormalizedRect.Unit)] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, moved);
    }
}
