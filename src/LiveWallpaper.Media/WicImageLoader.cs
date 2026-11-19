using System.Drawing;
using SharpGen.Runtime;
using Vortice.WIC;

namespace LiveWallpaper.Media;

public static class WicImageLoader
{
    public static Size ReadSize(string path)
    {
        return Decode(path, (_, frame) =>
        {
            var size = frame.Size;
            return new Size(size.Width, size.Height);
        });
    }

    public static DecodedImage Load(string path, Size decodeSize)
    {
        if (decodeSize.Width <= 0 || decodeSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decodeSize), decodeSize, "Decode size must be positive.");
        }

        return Decode(path, (factory, frame) =>
        {
            using var scaler = factory.CreateBitmapScaler();
            scaler.Initialize(frame, (uint)decodeSize.Width, (uint)decodeSize.Height, BitmapInterpolationMode.Fant);
            using var converter = factory.CreateFormatConverter();
            converter.Initialize(scaler, PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom).CheckError();

            var stride = decodeSize.Width * 4;
            var pixels = new byte[(long)stride * decodeSize.Height];
            converter.CopyPixels((uint)stride, pixels);
            return new DecodedImage(decodeSize.Width, decodeSize.Height, stride, pixels);
        });
    }

    private static T Decode<T>(string path, Func<IWICImagingFactory, IWICBitmapFrameDecode, T> read)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new ImageLoadException($"The image '{name}' was not found.");
        }

        try
        {
            using var factory = new IWICImagingFactory();
            using var decoder = factory.CreateDecoderFromFileName(path);
            using var frame = decoder.GetFrame(0);
            return read(factory, frame);
        }
        catch (SharpGenException ex)
        {
            throw new ImageLoadException($"'{name}' isn't an image Windows can read, or the file is damaged.", ex);
        }
        catch (IOException ex)
        {
            throw new ImageLoadException($"'{name}' couldn't be read.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImageLoadException($"Access to '{name}' was denied.", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new ImageLoadException($"'{name}' is too large to load.", ex);
        }
    }
}
