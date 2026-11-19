using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.App.Views;

public sealed class LibraryCard(LibraryItem item, string thumbnailPath, bool isOnDesktop)
{
    public LibraryItem Item { get; } = item;

    public string Name => Item.DisplayName;

    public ImageSource? Thumbnail { get; } = LoadThumbnail(thumbnailPath);

    public Visibility SnowBadge => Item.Snow.Enabled && Item.Snow.DensityPct > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DesktopBadge => isOnDesktop ? Visibility.Visible : Visibility.Collapsed;

    private static BitmapImage? LoadThumbnail(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 528;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
