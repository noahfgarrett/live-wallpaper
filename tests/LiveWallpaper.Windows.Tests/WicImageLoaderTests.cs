using System.Drawing;
using LiveWallpaper.Media;
using Vortice.WIC;

namespace LiveWallpaper.Windows.Tests;

public sealed class WicImageLoaderTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private string Png(int width, int height)
    {
        var path = TestImages.TempFile(".png");
        _files.Add(path);
        TestImages.WritePng(path, width, height, 0, 0, 255, 255);
        return path;
    }

    [Fact]
    public void Load_ValidPng_ReturnsPremultipliedBgra()
    {
        WindowsOnly.Require();

        var image = WicImageLoader.Load(Png(4, 2), new Size(4, 2));

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(16, image.Stride);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Pixels[..4]);
    }

    [Fact]
    public void Load_DownscalesToDecodeSize()
    {
        WindowsOnly.Require();

        var image = WicImageLoader.Load(Png(64, 32), new Size(16, 8));

        Assert.Equal((16, 8), (image.Width, image.Height));
        Assert.Equal(16 * 8 * 4, image.Pixels.Length);
    }

    [Fact]
    public void ReadSize_ReturnsPixelDimensions()
    {
        WindowsOnly.Require();

        Assert.Equal(new Size(64, 32), WicImageLoader.ReadSize(Png(64, 32)));
    }

    [Fact]
    public void Load_CorruptFile_ThrowsImageLoadException()
    {
        WindowsOnly.Require();
        var path = TestImages.TempFile(".png");
        _files.Add(path);
        File.WriteAllBytes(path, [0x13, 0x37, 0x00, 0xFF, 0x42, 0x42, 0x42, 0x42]);

        Assert.Throws<ImageLoadException>(() => WicImageLoader.ReadSize(path));
    }

    [Fact]
    public void Load_MissingFile_ThrowsImageLoadException()
    {
        WindowsOnly.Require();

        var ex = Assert.Throws<ImageLoadException>(() => WicImageLoader.Load(TestImages.TempFile(".png"), new Size(4, 4)));
        Assert.DoesNotContain(Path.GetTempPath(), ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_TextFileRenamedToJpg_ThrowsImageLoadException()
    {
        WindowsOnly.Require();
        var path = TestImages.TempFile(".jpg");
        _files.Add(path);
        File.WriteAllText(path, "this is not a picture");

        Assert.Throws<ImageLoadException>(() => WicImageLoader.Load(path, new Size(4, 4)));
    }

    private string Halves(ContainerFormat format, string extension, ushort? orientation = null)
    {
        var path = TestImages.TempFile(extension);
        _files.Add(path);
        TestImages.WriteHalves(path, 16, 8, format, orientation);
        return path;
    }

    private static (byte B, byte G, byte R) PixelAt(DecodedImage image, int x, int y)
    {
        var i = (y * image.Stride) + (x * 4);
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    [Fact]
    public void Load_WithSourceCrop_ReturnsOnlyThatRegion()
    {
        WindowsOnly.Require();

        var image = WicImageLoader.Load(Halves(ContainerFormat.Png, ".png"), new Rectangle(8, 0, 8, 8), new Size(8, 8));

        Assert.Equal((255, 0, 0), PixelAt(image, 0, 0));
        Assert.Equal((255, 0, 0), PixelAt(image, 7, 7));
    }

    [Fact]
    public void ReadSize_ExifRotated90_SwapsDimensions()
    {
        WindowsOnly.Require();

        Assert.Equal(new Size(8, 16), WicImageLoader.ReadSize(Halves(ContainerFormat.Jpeg, ".jpg", orientation: 6)));
    }

    [Fact]
    public void Load_ExifRotated90_PutsTheLeftHalfOnTop()
    {
        WindowsOnly.Require();

        var image = WicImageLoader.Load(Halves(ContainerFormat.Jpeg, ".jpg", orientation: 6), new Size(8, 16));

        var top = PixelAt(image, 4, 2);
        var bottom = PixelAt(image, 4, 13);
        Assert.True(top.R > 200 && top.B < 60, $"top pixel was {top}");
        Assert.True(bottom.B > 200 && bottom.R < 60, $"bottom pixel was {bottom}");
    }

    [Fact]
    public void Load_ExifRotated180_SwapsLeftAndRight()
    {
        WindowsOnly.Require();

        var image = WicImageLoader.Load(Halves(ContainerFormat.Jpeg, ".jpg", orientation: 3), new Size(16, 8));

        var left = PixelAt(image, 2, 4);
        Assert.True(left.B > 200 && left.R < 60, $"left pixel was {left}");
    }

    [Fact]
    public void ReadSize_JpegWithoutOrientation_KeepsDimensions()
    {
        WindowsOnly.Require();

        Assert.Equal(new Size(16, 8), WicImageLoader.ReadSize(Halves(ContainerFormat.Jpeg, ".jpg")));
    }
}
