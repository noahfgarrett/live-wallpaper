using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class RegionSetValidationTests
{
    private static RegionSet WithPoints(params RegionPoint[] points) => new([new Region(points)]);

    private static RegionPoint[] Polygon(int count) =>
        Enumerable.Range(0, count).Select(i => new RegionPoint(0.5 + (0.4 * Math.Cos(i * 0.1)), 0.5 + (0.4 * Math.Sin(i * 0.1)))).ToArray();

    [Fact]
    public void Empty_IsValid() => Assert.True(RegionSet.Empty.IsValid);

    [Fact]
    public void Quad_IsValid() => Assert.True(new RegionSet([RegionGeometry.CreateQuad()], Invert: true, FeatherPct: 10).IsValid);

    [Fact]
    public void NaNCoordinate_IsInvalid() => Assert.False(WithPoints(new(0.1, 0.1), new(double.NaN, 0.2), new(0.3, 0.1)).IsValid);

    [Fact]
    public void CoordinateOutsideImage_IsInvalid() => Assert.False(WithPoints(new(0.1, 0.1), new(1.2, 0.2), new(0.3, 0.1)).IsValid);

    [Fact]
    public void TwoPoints_IsInvalid() => Assert.False(WithPoints(new(0.1, 0.1), new(0.2, 0.2)).IsValid);

    [Fact]
    public void MaxPoints_IsValid() => Assert.True(WithPoints(Polygon(RegionSet.MaxPoints)).IsValid);

    [Fact]
    public void TooManyPoints_IsInvalid() => Assert.False(WithPoints(Polygon(RegionSet.MaxPoints + 1)).IsValid);

    [Fact]
    public void TooManyRegions_IsInvalid() =>
        Assert.False(new RegionSet(Enumerable.Repeat(RegionGeometry.CreateQuad(), RegionSet.MaxRegions + 1).ToArray()).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(10.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FeatherOutOfRange_IsInvalid(double featherPct) =>
        Assert.False(new RegionSet([RegionGeometry.CreateQuad()], FeatherPct: featherPct).IsValid);

    [Fact]
    public void NullRegionList_IsInvalid() => Assert.False(new RegionSet(null!).IsValid);

    [Fact]
    public void NullRegion_IsInvalid() => Assert.False(new RegionSet([null!]).IsValid);

    [Fact]
    public void NullPointList_IsInvalid() => Assert.False(new RegionSet([new Region(null!)]).IsValid);
}
