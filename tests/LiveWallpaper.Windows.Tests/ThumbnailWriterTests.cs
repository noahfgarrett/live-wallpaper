using LiveWallpaper.Media;
using Vortice.WIC;

namespace LiveWallpaper.Windows.Tests;

public sealed class ThumbnailWriterTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private string Temp(string extension)
    {
        var path = TestImages.TempFile(extension);
        _files.Add(path);
        return path;
    }

    [Fact]
    public void ThumbnailWriter_WritesJpegWithinMaxEdge()
    {
        WindowsOnly.Require();
        var source = Temp(".png");
        TestImages.WriteHalves(source, 1600, 900, ContainerFormat.Png);
        var thumb = Temp(".jpg");

        ThumbnailWriter.Write(source, thumb, 480);

        Assert.Equal(new System.Drawing.Size(480, 270), WicImageLoader.ReadSize(thumb));
    }

    [Fact]
    public void ThumbnailWriter_HonoursOrientation()
    {
        WindowsOnly.Require();
        var source = Temp(".jpg");
        TestImages.WriteHalves(source, 64, 32, ContainerFormat.Jpeg, exifOrientation: 6);
        var thumb = Temp(".jpg");

        ThumbnailWriter.Write(source, thumb, 480);

        var size = WicImageLoader.ReadSize(thumb);
        Assert.True(size.Height > size.Width, $"thumbnail was {size}");
    }

    [Fact]
    public void ThumbnailWriter_NotAnImage_ThrowsImageLoadException()
    {
        WindowsOnly.Require();
        var source = Temp(".jpg");
        File.WriteAllText(source, "not a picture");

        Assert.Throws<ImageLoadException>(() => ThumbnailWriter.Write(source, Temp(".jpg")));
    }
}
