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
    private readonly UpdateCoalescer<SnowSettings> _liveSnow;
    private readonly UpdateCoalescer<RainSettings> _liveRain;
    private readonly UpdateCoalescer<GlassSettings> _liveGlass;
    private readonly UpdateCoalescer<WaterSettings> _liveWater;
    private readonly Dictionary<EffectKind, UpdateCoalescer<RegionSet>> _liveRegions = [];
    private readonly DispatcherTimer _featherCommit = new() { Interval = FeatherCommitDelay };
    private readonly Dictionary<EffectKind, RegionEditor> _editors = [];
    private readonly double _aspect;
    private readonly System.Drawing.Size _monitorSize;
    private readonly System.Drawing.Size _renderSize;
    private PreviewCamera _camera;
    private LibraryItem _item;
    private EffectKind _activeTab = EffectKind.Snow;
    private bool _loading = true;
    private bool _syncingRegions;
    private bool _isSpaceSwallowed;

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
        // The preview draws the desktop at its own resolution; the camera picks the part of it on screen.
        _renderSize = PreviewCamera.RenderSizeFor(_monitorSize);
        _camera = PreviewCamera.Fit(_renderSize, Preview.PixelSize);
        _liveSnow = new UpdateCoalescer<SnowSettings>(snow => _engine.SendAsync(new UpdateSnow(_item.Id, snow), CancellationToken.None), LiveUpdateInterval);
        _liveRain = new UpdateCoalescer<RainSettings>(rain => _engine.SendAsync(new UpdateRain(_item.Id, rain), CancellationToken.None), LiveUpdateInterval);
        _liveGlass = new UpdateCoalescer<GlassSettings>(glass => _engine.SendAsync(new UpdateGlass(_item.Id, glass), CancellationToken.None), LiveUpdateInterval);
        _liveWater = new UpdateCoalescer<WaterSettings>(water => _engine.SendAsync(new UpdateWater(_item.Id, water), CancellationToken.None), LiveUpdateInterval);
        _saveTimer.Tick += (_, _) => Save();
        // The flow arrows are clamped in the source image's own proportions.
        var imageAspect = item.PixelWidth > 0 && item.PixelHeight > 0 ? item.PixelWidth / (double)item.PixelHeight : 1;
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            var editor = new RegionEditor(RegionsOf(item, kind), ModeOf(kind), imageAspect);
            editor.Changed += (_, _) => OnRegionsChanged(kind);
            _editors[kind] = editor;
            // One coalescer per effect, so a change to one effect's regions never replaces another's pending update.
            _liveRegions[kind] = new UpdateCoalescer<RegionSet>(
                regions => _engine.SendAsync(new UpdateRegions(_item.Id, kind, regions), CancellationToken.None), LiveUpdateInterval);
        }
        _featherCommit.Tick += (_, _) => CommitFeather();
        Preview.PointerInput += OnPreviewPointer;
        Preview.PixelSizeChanged += (_, _) => ShowCamera(_camera.Resize(Preview.PixelSize), force: true);
        Preview.ZoomRequested += (_, e) => ShowCamera(_camera.ZoomAt(e.ViewPoint, e.Factor));
        Preview.PanRequested += (_, e) => ShowCamera(_camera.Pan(e.Delta));
        // Focus leaves the editor when the window deactivates, so a swallowed Space's release may never arrive here.
        IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _isSpaceSwallowed = false;
            }
        };
        ApplyPanel.Closed += (_, _) =>
        {
            Overlay.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Visible;
        };

        Heading.Text = item.DisplayName;
        SnowEnabled.IsChecked = item.Snow.Enabled;
        SnowDensity.Value = item.Snow.DensityPct;
        SnowSpeed.Value = item.Snow.SpeedPct;
        SnowWind.Value = item.Snow.WindPct;
        SnowFlakeSize.Value = item.Snow.SizePct;
        MotionBlur.IsChecked = item.Snow.MotionBlur;
        var rain = item.Rain.Settings;
        RainEnabled.IsChecked = rain.Enabled;
        RainDensity.Value = rain.DensityPct;
        RainSpeed.Value = rain.SpeedPct;
        RainWind.Value = rain.WindPct;
        RainLength.Value = rain.LengthPct;
        RainBrightness.Value = rain.BrightnessPct;
        var glass = item.Glass.Settings;
        GlassEnabled.IsChecked = glass.Enabled;
        GlassDrops.Value = glass.DropsPct;
        GlassDropSize.Value = glass.DropSizePct;
        GlassSliding.Value = glass.SlidingPct;
        GlassFog.Value = glass.FogPct;
        var water = item.Water.Settings;
        WaterEnabled.IsChecked = water.Enabled;
        WaterStretch.Value = water.StretchPct;
        WaterFoam.Value = water.FoamPct;
        WaterMist.Value = water.MistPct;
        var scaling = window.LastStatus?.Settings.DefaultScaling ?? WallpaperScaling.Fill;
        SelectScaling(scaling);
        Preview.SetFrameRate(window.LastStatus?.Settings.FrameRate ?? FrameRateMode.Fps60);
        Preview.SetSnow(item.Snow);
        Preview.SetRain(rain);
        Preview.SetGlass(glass);
        Preview.SetWater(water);
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            Preview.SetRegions(kind, RegionsOf(item, kind));
        }
        Preview.SetImage(library.ImagePath(item), scaling, _monitorSize);
        _loading = false;
        UpdateLabels();
        UpdateTabDots();
        ShowTabLayout();
        ShowCamera(_camera, force: true);
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

    private RegionEditor ActiveEditor => _editors[_activeTab];

    private static RegionEditMode ModeOf(EffectKind kind) => kind switch
    {
        EffectKind.Snow or EffectKind.Rain => RegionEditMode.Free,
        EffectKind.Glass => RegionEditMode.Plane,
        EffectKind.Water => RegionEditMode.Flow,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown effect."),
    };

    private static RegionSet RegionsOf(LibraryItem item, EffectKind kind) => kind switch
    {
        EffectKind.Snow => item.Regions,
        EffectKind.Rain => item.Rain.Regions,
        EffectKind.Glass => item.Glass.Regions,
        EffectKind.Water => item.Water.Regions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown effect."),
    };

    private static LibraryItem WithRegions(LibraryItem item, EffectKind kind, RegionSet regions) => kind switch
    {
        EffectKind.Snow => item with { Regions = regions },
        EffectKind.Rain => item with { Rain = item.Rain with { Regions = regions } },
        EffectKind.Glass => item with { Glass = item.Glass with { Regions = regions } },
        EffectKind.Water => item with { Water = item.Water with { Regions = regions } },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown effect."),
    };

    private SnowSettings CurrentSnow() => new(
        SnowEnabled.IsChecked == true,
        Math.Round(SnowDensity.Value),
        Math.Round(SnowSpeed.Value),
        Math.Round(SnowWind.Value),
        Math.Round(SnowFlakeSize.Value),
        MotionBlur.IsChecked == true);

    private RainSettings CurrentRain() => new(
        RainEnabled.IsChecked == true,
        Math.Round(RainDensity.Value),
        Math.Round(RainSpeed.Value),
        Math.Round(RainWind.Value),
        Math.Round(RainLength.Value),
        Math.Round(RainBrightness.Value));

    private GlassSettings CurrentGlass() => new(
        GlassEnabled.IsChecked == true,
        Math.Round(GlassDrops.Value),
        Math.Round(GlassDropSize.Value),
        Math.Round(GlassSliding.Value),
        Math.Round(GlassFog.Value));

    private WaterSettings CurrentWater() => new(
        WaterEnabled.IsChecked == true,
        Math.Round(WaterStretch.Value),
        Math.Round(WaterFoam.Value),
        Math.Round(WaterMist.Value));

    private void OnSnowSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnSnowChanged(sender, e);

    private void OnRainSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnRainChanged(sender, e);

    private void OnGlassSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnGlassChanged(sender, e);

    private void OnWaterSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnWaterChanged(sender, e);

    private void OnSnowChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var snow = CurrentSnow();
        Preview.SetSnow(snow);
        _item = _item with { Snow = snow };
        OnSettingsChanged();
        if (IsOnDesktop)
        {
            _liveSnow.Post(snow);
        }
    }

    private void OnRainChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var rain = CurrentRain();
        Preview.SetRain(rain);
        _item = _item with { Rain = _item.Rain with { Settings = rain } };
        OnSettingsChanged();
        if (IsOnDesktop)
        {
            _liveRain.Post(rain);
        }
    }

    private void OnGlassChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var glass = CurrentGlass();
        Preview.SetGlass(glass);
        _item = _item with { Glass = _item.Glass with { Settings = glass } };
        OnSettingsChanged();
        if (IsOnDesktop)
        {
            _liveGlass.Post(glass);
        }
    }

    private void OnWaterChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var water = CurrentWater();
        Preview.SetWater(water);
        _item = _item with { Water = _item.Water with { Settings = water } };
        OnSettingsChanged();
        if (IsOnDesktop)
        {
            _liveWater.Post(water);
        }
    }

    private void OnSettingsChanged()
    {
        UpdateLabels();
        UpdateTabDots();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: EffectKind kind } || kind == _activeTab)
        {
            return;
        }
        CommitFeather();
        var previous = ActiveEditor;
        _activeTab = kind;
        // Ends a drag in progress on the tab being left; its regions stay as they were before the drag.
        previous.SetEnabled(false);
        previous.SetEnabled(ShowOutlines.IsChecked == true);
        ShowTabLayout();
        Preview.SetOverlay(ActiveEditor.Overlay(), (float)DpiScale);
        SyncRegionControls();
    }

    private void ShowTabLayout()
    {
        SnowPanel.Visibility = _activeTab == EffectKind.Snow ? Visibility.Visible : Visibility.Collapsed;
        RainPanel.Visibility = _activeTab == EffectKind.Rain ? Visibility.Visible : Visibility.Collapsed;
        GlassPanel.Visibility = _activeTab == EffectKind.Glass ? Visibility.Visible : Visibility.Collapsed;
        WaterPanel.Visibility = _activeTab == EffectKind.Water ? Visibility.Visible : Visibility.Collapsed;
        var copy = RegionCopy.For(_activeTab);
        RegionsHelp.Text = copy.Help;
        AddRegionButton.Content = copy.Add;
        DeleteRegionButton.Content = copy.Delete;
        InvertRow.Visibility = copy.Invert is null ? Visibility.Collapsed : Visibility.Visible;
        InvertLabel.Text = copy.Invert ?? string.Empty;
        // A pane always has 4 corners, so corners can't be added or removed on the glass tab.
        CornerHelp.Visibility = ModeOf(_activeTab) == RegionEditMode.Plane ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateTabDots()
    {
        SnowDot.Visibility = SnowEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RainDot.Visibility = RainEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GlassDot.Visibility = GlassEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WaterDot.Visibility = WaterEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnScalingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            Preview.SetImage(_library.ImagePath(_item), SelectedScaling(), _monitorSize);
            ShowCamera(_camera.ToFit(), force: true);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateRegionView();
    }

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Makes <paramref name="camera"/> the preview's view and updates everything that depends on it; does nothing if it hasn't changed, unless forced.</summary>
    private void ShowCamera(PreviewCamera camera, bool force = false)
    {
        if (!force && camera == _camera)
        {
            return;
        }
        _camera = camera;
        Preview.SetCamera(camera);
        ZoomText.Text = $"{camera.ZoomPercent} %";
        ZoomOutButton.IsEnabled = camera.CanZoomOut;
        ZoomInButton.IsEnabled = camera.CanZoomIn;
        FitButton.IsEnabled = camera.CanZoomOut;
        UpdateRegionView();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e) => ShowCamera(_camera.StepOut());

    private void OnZoomIn(object sender, RoutedEventArgs e) => ShowCamera(_camera.StepIn());

    private void OnZoomFit(object sender, RoutedEventArgs e) => ShowCamera(_camera.ToFit());

    private void UpdateRegionView()
    {
        var pixels = Preview.PixelSize;
        RegionView? view = null;
        if (pixels.Width >= 2 && pixels.Height >= 2 && _item.PixelWidth > 0 && _item.PixelHeight > 0)
        {
            // Where the whole image lands on the desktop-sized scene, then where the camera shows that.
            var imageBoundsOnRender = ScalingMath.ComputeScaledDestination(new System.Drawing.Size(_item.PixelWidth, _item.PixelHeight), _monitorSize, _renderSize, SelectedScaling());
            view = new RegionView(_camera.MapRect(imageBoundsOnRender), pixels, DpiScale);
        }
        foreach (var editor in _editors.Values)
        {
            editor.SetView(view);
        }
    }

    private void OnRegionsChanged(EffectKind kind)
    {
        var editor = _editors[kind];
        var state = editor.State;
        Preview.SetRegions(kind, state);
        if (kind == _activeTab)
        {
            Preview.SetOverlay(editor.Overlay(), (float)DpiScale);
            SyncRegionControls();
        }
        if (state.Equals(RegionsOf(_item, kind)))
        {
            return;
        }
        _item = WithRegions(_item, kind, state);
        _saveTimer.Stop();
        _saveTimer.Start();
        if (IsOnDesktop)
        {
            _liveRegions[kind].Post(state);
        }
    }

    private void SyncRegionControls()
    {
        var editor = ActiveEditor;
        var state = editor.State;
        _syncingRegions = true;
        InvertRegions.IsChecked = state.Invert;
        if (!_featherCommit.IsEnabled && Math.Abs(Feather.Value - state.FeatherPct) > 1e-9)
        {
            Feather.Value = state.FeatherPct;
        }
        _syncingRegions = false;
        FeatherValue.Text = $"{state.FeatherPct:0.0}%";
        AddRegionButton.IsEnabled = editor.CanAddRegion;
        DeleteRegionButton.IsEnabled = editor.SelectedRegion is not null;
        UndoButton.IsEnabled = editor.CanUndo;
        RedoButton.IsEnabled = editor.CanRedo;
    }

    private void OnPreviewPointer(object? sender, PreviewPointerEventArgs e)
    {
        var editor = ActiveEditor;
        switch (e.Action)
        {
            case PreviewPointerAction.Pressed:
                Focus();
                editor.PointerDown(e.Position);
                break;
            case PreviewPointerAction.Moved:
                editor.PointerMove(e.Position);
                break;
            case PreviewPointerAction.Released:
                editor.PointerUp();
                break;
            case PreviewPointerAction.SecondaryClicked:
                editor.RemoveVertexAt(e.Position);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e.Action, "Unknown pointer action.");
        }
    }

    private void OnAddRegion(object sender, RoutedEventArgs e)
    {
        ShowOutlines.IsChecked = true;
        ActiveEditor.AddRegion();
    }

    private void OnDeleteRegion(object sender, RoutedEventArgs e) => ActiveEditor.DeleteSelectedRegion();

    private void OnInvertChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && !_syncingRegions)
        {
            ActiveEditor.SetInvert(InvertRegions.IsChecked == true);
        }
    }

    private void OnFeatherChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _syncingRegions)
        {
            return;
        }
        ActiveEditor.SetFeather(Math.Round(Feather.Value, 1), commit: false);
        _featherCommit.Stop();
        _featherCommit.Start();
    }

    /// <summary>Commits a pending soft-edge change, if there is one, to the open tab's regions.</summary>
    private void CommitFeather()
    {
        if (!_featherCommit.IsEnabled)
        {
            return;
        }
        _featherCommit.Stop();
        ActiveEditor.SetFeather(Math.Round(Feather.Value, 1), commit: true);
    }

    private void OnOutlinesChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        // Shared by all tabs, so every effect's editor follows it.
        var isShown = ShowOutlines.IsChecked == true;
        foreach (var editor in _editors.Values)
        {
            editor.SetEnabled(isShown);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        _featherCommit.Stop();
        ActiveEditor.Undo();
    }

    private void OnRedo(object sender, RoutedEventArgs e) => ActiveEditor.Redo();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Overlay.Visibility == Visibility.Visible)
        {
            return;
        }
        if (e.Key == Key.Space && Preview.IsUnderCursor())
        {
            // Space over the preview is for panning. A focused switch or button would otherwise take it, capture the
            // mouse so the preview never sees the drag, and click when Space comes up.
            Focus();
            _isSpaceSwallowed = true;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && !e.IsRepeat)
        {
            // A fresh Space press off the preview: any earlier swallowed press has ended, wherever its release went.
            _isSpaceSwallowed = false;
        }
        var modifiers = Keyboard.Modifiers;
        if (e.Key is Key.Delete or Key.Back && modifiers == ModifierKeys.None)
        {
            e.Handled = ActiveEditor.DeleteSelectedVertex();
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
        else if (modifiers == ModifierKeys.Control && ZoomFor(e.Key) is { } zoom)
        {
            ShowCamera(zoom);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _isSpaceSwallowed)
        {
            _isSpaceSwallowed = false;
            e.Handled = true;
        }
    }

    /// <summary>The camera a Ctrl+key zoom shortcut asks for, or null if the key isn't one.</summary>
    private PreviewCamera? ZoomFor(Key key) => key switch
    {
        Key.D0 or Key.NumPad0 => _camera.ToFit(),
        Key.D1 or Key.NumPad1 => _camera.ToActual(),
        Key.OemPlus or Key.Add => _camera.StepIn(),
        Key.OemMinus or Key.Subtract => _camera.StepOut(),
        _ => null,
    };

    private void OnPreviewAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var available = Math.Max(0, PreviewArea.ActualHeight - (ZoomBar.Height + ZoomBar.Margin.Top + ZoomBar.Margin.Bottom));
        var width = PreviewArea.ActualWidth;
        var height = width / _aspect;
        if (height > available)
        {
            height = available;
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

    private void UpdateLabels()
    {
        SnowDensityValue.Text = Percent(SnowDensity);
        SnowSpeedValue.Text = Percent(SnowSpeed);
        SnowWindValue.Text = Percent(SnowWind);
        SnowSizeValue.Text = Percent(SnowFlakeSize);
        RainDensityValue.Text = Percent(RainDensity);
        RainSpeedValue.Text = Percent(RainSpeed);
        RainWindValue.Text = Percent(RainWind);
        RainLengthValue.Text = Percent(RainLength);
        RainBrightnessValue.Text = Percent(RainBrightness);
        GlassDropsValue.Text = Percent(GlassDrops);
        GlassDropSizeValue.Text = Percent(GlassDropSize);
        GlassSlidingValue.Text = Percent(GlassSliding);
        GlassFogValue.Text = Percent(GlassFog);
        WaterStretchValue.Text = Percent(WaterStretch);
        WaterFoamValue.Text = Percent(WaterFoam);
        WaterMistValue.Text = Percent(WaterMist);
    }

    private static string Percent(Slider slider) => $"{Math.Round(slider.Value)}%";

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

    /// <summary>The Regions panel's wording for one tab. <see cref="Invert"/> is null where the effect can't be inverted.</summary>
    private sealed record RegionCopy(string? Invert, string Add, string Delete, string Help)
    {
        public static RegionCopy For(EffectKind kind) => kind switch
        {
            EffectKind.Snow => new("Snow everywhere except the regions", "Add region", "Delete region",
                "Without regions, snow covers the whole picture. Add a region and drag its corners onto a window or the sky."),
            EffectKind.Rain => new("Rain everywhere except the regions", "Add region", "Delete region",
                "Without regions, rain covers the whole picture."),
            EffectKind.Glass => new(null, "Add pane", "Delete pane",
                "Without panes, the whole picture is the glass. Add a pane and drag its corners onto a window."),
            EffectKind.Water => new(null, "Add water region", "Delete region",
                "Add a region on a waterfall or river, then drag the arrow the way the water flows. A longer arrow flows faster."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown effect."),
        };
    }
}
