using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveWallpaper.App.Services;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Views;

public partial class EditorView : UserControl
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromMilliseconds(33);

    private readonly MainWindow _window;
    private readonly EngineConnection _engine;
    private readonly LibraryService _library;
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };
    private readonly UpdateCoalescer<SnowSettings> _liveUpdates;
    private readonly double _aspect;
    private LibraryItem _item;
    private bool _loading = true;

    public EditorView(MainWindow window, EngineConnection engine, LibraryService library, LibraryItem item)
    {
        InitializeComponent();
        _window = window;
        _engine = engine;
        _library = library;
        _item = item;
        var primary = MonitorEnumerator.Enumerate().FirstOrDefault(m => m.IsPrimary);
        _aspect = primary is null || primary.Bounds.Height == 0 ? 16.0 / 9 : primary.Bounds.Width / (double)primary.Bounds.Height;
        _liveUpdates = new UpdateCoalescer<SnowSettings>(SendLiveAsync, LiveUpdateInterval);
        _saveTimer.Tick += (_, _) => Save();
        ApplyPanel.Closed += (_, _) =>
        {
            Overlay.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Visible;
        };

        Heading.Text = item.DisplayName;
        SnowEnabled.IsChecked = item.Snow.Enabled;
        Density.Value = item.Snow.DensityPct;
        Speed.Value = item.Snow.SpeedPct;
        Wind.Value = item.Snow.WindPct;
        FlakeSize.Value = item.Snow.SizePct;
        MotionBlur.IsChecked = item.Snow.MotionBlur;
        var scaling = window.LastStatus?.Settings.DefaultScaling ?? WallpaperScaling.Fill;
        SelectScaling(scaling);
        Preview.SetFrameRate(window.LastStatus?.Settings.FrameRate ?? FrameRateMode.Fps60);
        Preview.SetSnow(item.Snow);
        Preview.SetImage(library.ImagePath(item), scaling);
        _loading = false;
        UpdateLabels();
    }

    public void Close()
    {
        if (_saveTimer.IsEnabled)
        {
            Save();
        }
        _saveTimer.Stop();
        Preview.Dispose();
    }

    private bool IsOnDesktop => _window.LastStatus?.Monitors.Any(m => m.IsShown && m.LibraryItemId == _item.Id) == true;

    private SnowSettings CurrentSnow() => new(
        SnowEnabled.IsChecked == true,
        Math.Round(Density.Value),
        Math.Round(Speed.Value),
        Math.Round(Wind.Value),
        Math.Round(FlakeSize.Value),
        MotionBlur.IsChecked == true);

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnSnowChanged(sender, e);

    private void OnSnowChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        UpdateLabels();
        var snow = CurrentSnow();
        Preview.SetSnow(snow);
        _item = _item with { Snow = snow };
        _saveTimer.Stop();
        _saveTimer.Start();
        if (IsOnDesktop)
        {
            _liveUpdates.Post(snow);
        }
    }

    private void OnScalingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            Preview.SetImage(_library.ImagePath(_item), SelectedScaling());
        }
    }

    private void OnPreviewAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = PreviewArea.ActualWidth;
        var height = width / _aspect;
        if (height > PreviewArea.ActualHeight)
        {
            height = PreviewArea.ActualHeight;
            width = height * _aspect;
        }
        Preview.Width = Math.Max(1, width - 2);
        Preview.Height = Math.Max(1, height - 2);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Save();
        Preview.Visibility = Visibility.Hidden;
        Overlay.Visibility = Visibility.Visible;
        ApplyPanel.Open(_item, SelectedScaling(), async (selector, scaling) =>
        {
            if (!await _engine.EnsureRunningAsync(CancellationToken.None))
            {
                return "The wallpaper engine couldn't be started.";
            }
            var response = await _engine.SendAsync(new ApplyWallpaper(_item.Id, selector, scaling), CancellationToken.None);
            if (response is Failed failed)
            {
                return failed.Message;
            }
            if (response is null)
            {
                return "The wallpaper engine didn't respond.";
            }
            await _window.RefreshEngineStatusAsync();
            StatusText.Text = "Applied. It's on your desktop now.";
            return null;
        });
    }

    private void OnBack(object sender, RoutedEventArgs e) => _window.ShowLibrary();

    private void Save()
    {
        _saveTimer.Stop();
        try
        {
            _library.Update(_item);
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (IOException ex)
        {
            Log.Error("app", "event=save-failed", ex);
            StatusText.Text = "Your changes couldn't be saved.";
        }
    }

    private Task SendLiveAsync(SnowSettings snow) => _engine.SendAsync(new UpdateSnow(_item.Id, snow), CancellationToken.None);

    private void UpdateLabels()
    {
        DensityValue.Text = $"{Math.Round(Density.Value)}%";
        SpeedValue.Text = $"{Math.Round(Speed.Value)}%";
        WindValue.Text = $"{Math.Round(Wind.Value)}%";
        SizeValue.Text = $"{Math.Round(FlakeSize.Value)}%";
    }

    private void SelectScaling(WallpaperScaling scaling)
    {
        foreach (ComboBoxItem option in ScalingChoice.Items)
        {
            if ((string)option.Tag == scaling.ToString())
            {
                ScalingChoice.SelectedItem = option;
            }
        }
    }

    private WallpaperScaling SelectedScaling() =>
        Enum.TryParse<WallpaperScaling>((ScalingChoice.SelectedItem as ComboBoxItem)?.Tag as string, out var scaling) ? scaling : WallpaperScaling.Fill;
}
