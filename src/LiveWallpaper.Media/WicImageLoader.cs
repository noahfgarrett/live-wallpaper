using System.Drawing;
using SharpGen.Runtime;
using Vortice.WIC;

namespace LiveWallpaper.Media;

public static class WicImageLoader
{
    private static readonly string[] s_orientationQueries = ["/app1/ifd/{ushort=274}", "/ifd/{ushort=274}"];

    public static Size ReadSize(string path)
    {
        return Decode(path, (_, frame) => OrientedSize(frame, ReadOrientation(frame)));
    }

    public static DecodedImage Load(string path, Size decodeSize) => Load(path, null, decodeSize);

    public static DecodedImage Load(string path, Rectangle? sourceCrop, Size decodeSize)
    {
        if (decodeSize.Width <= 0 || decodeSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decodeSize), decodeSize, "Decode size must be positive.");
        }

        return Decode(path, (factory, frame) =>
        {
            var orientation = ReadOrientation(frame);
            var oriented = OrientedSize(frame, orientation);
            var disposables = new List<IDisposable>();
            try
            {
                IWICBitmapSource source = frame;
                if (orientation != BitmapTransformOptions.Rotate0)
                {
                    var rotator = factory.CreateBitmapFlipRotator();
                    disposables.Add(rotator);
                    rotator.Initialize(source, orientation);
                    source = rotator;
                }

                var crop = sourceCrop ?? new Rectangle(Point.Empty, oriented);
                if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 || crop.Right > oriented.Width || crop.Bottom > oriented.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(sourceCrop), crop, "Crop must lie inside the image.");
                }
                if (crop.Size != oriented)
                {
                    var clipper = factory.CreateBitmapClipper();
                    disposables.Add(clipper);
                    clipper.Initialize(source, new Vortice.Mathematics.RectI(crop.X, crop.Y, crop.Width, crop.Height)).CheckError();
                    source = clipper;
                }

                var scaler = factory.CreateBitmapScaler();
                disposables.Add(scaler);
                scaler.Initialize(source, (uint)decodeSize.Width, (uint)decodeSize.Height, BitmapInterpolationMode.Fant);
                var converter = factory.CreateFormatConverter();
                disposables.Add(converter);
                converter.Initialize(scaler, PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom).CheckError();

                var stride = decodeSize.Width * 4;
                var pixels = new byte[(long)stride * decodeSize.Height];
                converter.CopyPixels((uint)stride, pixels);
                return new DecodedImage(decodeSize.Width, decodeSize.Height, stride, pixels);
            }
            finally
            {
                for (var i = disposables.Count - 1; i >= 0; i--)
                {
                    disposables[i].Dispose();
                }
            }
        });
    }

    private static BitmapTransformOptions ReadOrientation(IWICBitmapFrameDecode frame)
    {
        IWICMetadataQueryReader? reader;
        try
        {
            reader = frame.MetadataQueryReader;
        }
        catch (SharpGenException)
        {
            return BitmapTransformOptions.Rotate0;
        }
        if (reader is null)
        {
            return BitmapTransformOptions.Rotate0;
        }

        using (reader)
        {
            foreach (var query in s_orientationQueries)
            {
                try
                {
                    var value = reader.GetMetadataByName(query).Value;
                    if (value is not null)
                    {
                        return ToTransform(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                catch (SharpGenException)
                {
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
            }
        }
        return BitmapTransformOptions.Rotate0;
    }

    private static BitmapTransformOptions ToTransform(int exifOrientation) => exifOrientation switch
    {
        2 => BitmapTransformOptions.FlipHorizontal,
        3 => BitmapTransformOptions.Rotate180,
        4 => BitmapTransformOptions.FlipVertical,
        5 => BitmapTransformOptions.Rotate270 | BitmapTransformOptions.FlipHorizontal,
        6 => BitmapTransformOptions.Rotate90,
        7 => BitmapTransformOptions.Rotate90 | BitmapTransformOptions.FlipHorizontal,
        8 => BitmapTransformOptions.Rotate270,
        _ => BitmapTransformOptions.Rotate0,
    };

    private static Size OrientedSize(IWICBitmapFrameDecode frame, BitmapTransformOptions orientation)
    {
        var size = frame.Size;
        var swaps = (orientation & BitmapTransformOptions.Rotate90) != 0;
        return swaps ? new Size(size.Height, size.Width) : new Size(size.Width, size.Height);
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
