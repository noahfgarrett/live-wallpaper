namespace LiveWallpaper.Media;

public sealed class ImageLoadException(string message, Exception? inner = null) : Exception(message, inner);
