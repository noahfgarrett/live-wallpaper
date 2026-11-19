using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveWallpaper.App.Preview;
using LiveWallpaper.App.Services;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Views;

public partial class EditorView : UserControl
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan FeatherCommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly MainWindow _window;
    private readonly EngineConnection _engine;
    private readonly LibraryService _library;
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };
    private readonly UpdateCoalescer<SnowSettings> _liveUpdates;
    private readonly UpdateCoalescer<RegionSet> _liveRegions;
    private readonly DispatcherTimer _featherCommit = new() { Interval = FeatherCommitDelay };
    private readonly RegionEditor _regions;
    private readonly double _aspect;
    private readonly System.Drawing.Size _monitorSize;
    private LibraryItem _item;
    private bool _loading = true;
    private bool _syncingRegions;

    public EditorView(MainWindow window, EngineConnection engine, LibraryService library, LibraryItem item)
    {
        InitializeComponent();
        _window = window;
        _engine = engine;
        _library = library;
        _item = item;
        var primary = MonitorEnumerator.Enumerate().FirstOrDefault(m => m.IsPrimary);
        _monitorSize = primary is null || primary.Bounds.Width <= 0 || primary.Bounds.Height <= 0
            ? new System.Drawing.Size(1920, 1080)
            : primary.Bounds.Size;
        _aspect = _monitorSize.Width / (double)_monitorSize.Height;
        _liveUpdates = new UpdateCoalescer<SnowSettings>(SendLiveAsync, LiveUpdateInterval);
        _liveRegions = new UpdateCoalescer<RegionSet>(SendRegionsAsync, LiveUpdateInterval);
        _saveTimer.Tick += (_, _) => Save();
        _regions = new RegionEditor(item.Regions);
        _regions.Changed += (_, _) => OnRegionsChanged();
        _featherCommit.Tick += (_, _) =>
        {
            _featherCommit.Stop();
            _regions.SetFeather(Math.Round(Feather.Value, 1), commit: true);
        };
        Preview.PointerInput += OnPreviewPointer;
        Preview.PixelSizeChanged += (_, _) => UpdateRegionView();
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
        Preview.SetRegions(item.Regions);
        Preview.SetImage(library.ImagePath(item), scaling, _monitorSize);
        _loading = false;
        UpdateLabels();
        UpdateRegionView();
        SyncRegionControls();
    }

    public void Close()
    {
        if (_saveTimer.IsEnabled)
        {
            Save();
        }
        _saveTimer.Stop();
        _featherCommit.Stop();
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
            Preview.SetImage(_library.ImagePath(_item), SelectedScaling(), _monitorSize);
            UpdateRegionView();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateRegionView();
    }

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void UpdateRegionView()
    {
        var pixels = Preview.PixelSize;
        if (pixels.Width < 2 || pixels.Height < 2 || _item.PixelWidth <= 0 || _item.PixelHeight <= 0)
        {
            _regions.SetView(null);
            return;
        }
        var imageBounds = ScalingMath.ComputeScaledDestination(new System.Drawing.Size(_item.PixelWidth, _item.PixelHeight), _monitorSize, pixels, SelectedScaling());
        _regions.SetView(new RegionView(imageBounds, pixels, DpiScale));
    }

    private void OnRegionsChanged()
    {
        var state = _regions.State;
        Preview.SetRegions(state);
        Preview.SetOverlay(_regions.Overlay(), (float)DpiScale);
        SyncRegionControls();
        if (state.Equals(_item.Regions))
        {
            return;
        }
        _item = _item with { Regions = state };
        _saveTimer.Stop();
        _saveTimer.Start();
        if (IsOnDesktop)
        {
            _liveRegions.Post(state);
        }
    }

    private void SyncRegionControls()
    {
        var state = _regions.State;
        _syncingRegions = true;
        InvertRegions.IsChecked = state.Invert;
        if (!_featherCommit.IsEnabled && Math.Abs(Feather.Value - state.FeatherPct) > 1e-9)
        {
            Feather.Value = state.FeatherPct;
        }
        _syncingRegions = false;
        FeatherValue.Text = $"{state.FeatherPct:0.0}%";
        AddRegionButton.IsEnabled = _regions.CanAddRegion;
        DeleteRegionButton.IsEnabled = _regions.SelectedRegion is not null;
        UndoButton.IsEnabled = _regions.CanUndo;
        RedoButton.IsEnabled = _regions.CanRedo;
    }

    private void OnPreviewPointer(object? sender, PreviewPointerEventArgs e)
    {
        switch (e.Action)
        {
            case PreviewPointerAction.Pressed:
                Focus();
                _regions.PointerDown(e.Position);
                break;
            case PreviewPointerAction.Moved:
                _regions.PointerMove(e.Position);
                break;
            case PreviewPointerAction.Released:
                _regions.PointerUp();
                break;
            case PreviewPointerAction.SecondaryClicked:
                _regions.RemoveVertexAt(e.Position);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e.Action, "Unknown pointer action.");
        }
    }

    private void OnAddRegion(object sender, RoutedEventArgs e)
    {
        ShowOutlines.IsChecked = true;
        _regions.AddRegion();
    }

    private void OnDeleteRegion(object sender, RoutedEventArgs e) => _regions.DeleteSelectedRegion();

    private void OnInvertChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && !_syncingRegions)
        {
            _regions.SetInvert(InvertRegions.IsChecked == true);
        }
    }

    private void OnFeatherChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _syncingRegions)
        {
            return;
        }
        _regions.SetFeather(Math.Round(Feather.Value, 1), commit: false);
        _featherCommit.Stop();
        _featherCommit.Start();
    }

    private void OnOutlinesChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _regions.SetEnabled(ShowOutlines.IsChecked == true);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        _featherCommit.Stop();
        _regions.Undo();
    }

    private void OnRedo(object sender, RoutedEventArgs e) => _regions.Redo();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Overlay.Visibility == Visibility.Visible)
        {
            return;
        }
        var modifiers = Keyboard.Modifiers;
        if (e.Key is Key.Delete or Key.Back && modifiers == ModifierKeys.None)
        {
            e.Handled = _regions.DeleteSelectedVertex();
        }
        else if (e.Key == Key.Z && modifiers == ModifierKeys.Control)
        {
            OnUndo(sender, e);
            e.Handled = true;
        }
        else if ((e.Key == Key.Y && modifiers == ModifierKeys.Control) || (e.Key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            OnRedo(sender, e);
            e.Handled = true;
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
            var start = await _engine.EnsureRunningAsync(CancellationToken.None);
            if (start != EngineStart.Running)
            {
                return EngineConnection.Describe(start);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("app", "event=save-failed", ex);
            StatusText.Text = "Your changes couldn't be saved.";
        }
    }

    private Task SendLiveAsync(SnowSettings snow) => _engine.SendAsync(new UpdateSnow(_item.Id, snow), CancellationToken.None);

    private Task SendRegionsAsync(RegionSet regions) => _engine.SendAsync(new UpdateRegions(_item.Id, EffectKind.Snow, regions), CancellationToken.None);

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
