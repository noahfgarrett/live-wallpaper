using System.Drawing;
using LiveWallpaper.Media;

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
}
