using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Tests.Snow;

public sealed class FlakeAtlasTests
{
    private const int Width = FlakeAtlas.CellPx * FlakeAtlas.Cells;

    private static byte Alpha(byte[] atlas, int x, int y) => atlas[((y * Width) + x) * 4 + 3];

    [Fact]
    public void Atlas_HasExpectedSize() =>
        Assert.Equal(Width * FlakeAtlas.CellPx * 4, FlakeAtlas.GenerateBgra().Length);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Cell_CentreOpaqueCornersClear(int cell)
    {
        var atlas = FlakeAtlas.GenerateBgra();
        var x0 = cell * FlakeAtlas.CellPx;
        var c = FlakeAtlas.CellPx / 2;

        Assert.True(Alpha(atlas, x0 + c, c) >= 200);
        Assert.Equal(0, Alpha(atlas, x0, 0));
        Assert.Equal(0, Alpha(atlas, x0 + FlakeAtlas.CellPx - 1, FlakeAtlas.CellPx - 1));
    }

    [Fact]
    public void Atlas_IsPremultipliedWhite()
    {
        var atlas = FlakeAtlas.GenerateBgra();
        for (var i = 0; i < atlas.Length; i += 4)
        {
            Assert.True(atlas[i] == atlas[i + 3] && atlas[i + 1] == atlas[i + 3] && atlas[i + 2] == atlas[i + 3]);
        }
    }
}
