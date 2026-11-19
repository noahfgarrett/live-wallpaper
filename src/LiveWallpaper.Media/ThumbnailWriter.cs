using System.Drawing;
using SharpGen.Runtime;
using Vortice.WIC;

namespace LiveWallpaper.Media;

public static class ThumbnailWriter
{
    public static void Write(string sourcePath, string destinationJpeg, int maxEdge = 480)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEdge, 16);
        var size = WicImageLoader.ReadSize(sourcePath);
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(size.Width, size.Height));
        var target = new Size(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        var image = WicImageLoader.Load(sourcePath, target);

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationJpeg));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var tempPath = destinationJpeg + ".tmp";
        try
        {
            using (var factory = new IWICImagingFactory())
            using (var stream = File.Create(tempPath))
            using (var encoder = factory.CreateEncoder(ContainerFormat.Jpeg, stream))
            using (var frame = encoder.CreateNewFrame(out var options))
            using (var bitmap = factory.CreateBitmapFromMemory((uint)image.Width, (uint)image.Height, PixelFormat.Format32bppPBGRA, image.Pixels, (uint)image.Stride))
            using (var converter = factory.CreateFormatConverter())
            {
                frame.Initialize(options);
                frame.SetSize((uint)image.Width, (uint)image.Height);
                frame.SetPixelFormat(PixelFormat.Format24bppBGR);
                converter.Initialize(bitmap, PixelFormat.Format24bppBGR).CheckError();
                frame.WriteSource(converter);
                frame.Commit();
                encoder.Commit();
                options?.Dispose();
            }
            File.Move(tempPath, destinationJpeg, overwrite: true);
        }
        catch (SharpGenException ex)
        {
            throw new ImageLoadException($"Couldn't create a thumbnail for '{Path.GetFileName(sourcePath)}'.", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
