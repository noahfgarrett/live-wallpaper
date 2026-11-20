using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.App.Views;

public sealed class LibraryCard(LibraryItem item, string thumbnailPath) : INotifyPropertyChanged
{
    private bool _isOnDesktop;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryItem Item { get; } = item;

    public string Name => Item.DisplayName;

    public ImageSource? Thumbnail { get; } = LoadThumbnail(thumbnailPath);

    public Visibility SnowBadge => Item.Snow.Enabled && Item.Snow.DensityPct > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RainBadge => Item.Rain.Settings.Enabled && Item.Rain.Settings.DensityPct > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlassBadge => Item.Glass.Settings.Enabled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Water only moves inside its regions, so without any it shows nothing.</summary>
    public Visibility WaterBadge => Item.Water.Settings.Enabled && Item.Water.Regions.Regions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DesktopBadge => _isOnDesktop ? Visibility.Visible : Visibility.Collapsed;

    public bool IsOnDesktop
    {
        get => _isOnDesktop;
        set
        {
            if (_isOnDesktop != value)
            {
                _isOnDesktop = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DesktopBadge)));
            }
        }
    }

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
