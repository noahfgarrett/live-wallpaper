using Vortice.WIC;

namespace LiveWallpaper.Windows.Tests;

internal static class TestImages
{
    public static void WritePng(string path, int width, int height, byte b, byte g, byte r, byte a)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        using var factory = new IWICImagingFactory();
        using var stream = File.Create(path);
        using var encoder = factory.CreateEncoder(ContainerFormat.Png, stream);
        using var frame = encoder.CreateNewFrame(out var options);
        frame.Initialize(options);
        frame.SetSize((uint)width, (uint)height);
        frame.SetPixelFormat(PixelFormat.Format32bppBGRA);
        using var bitmap = factory.CreateBitmapFromMemory((uint)width, (uint)height, PixelFormat.Format32bppBGRA, pixels, (uint)stride);
        frame.WriteSource(bitmap);
        frame.Commit();
        encoder.Commit();
        options?.Dispose();
    }

    public static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), $"lw-{Guid.NewGuid():N}{extension}");
}
