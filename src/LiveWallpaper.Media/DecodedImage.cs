namespace LiveWallpaper.Media;

public sealed record DecodedImage(int Width, int Height, int Stride, byte[] Pixels);
