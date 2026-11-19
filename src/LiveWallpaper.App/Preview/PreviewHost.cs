using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Preview;

public enum PreviewPointerAction
{
    Pressed,
    Moved,
    Released,
    SecondaryClicked,
}

/// <summary>Mouse input on the preview, in its client pixels.</summary>
public sealed class PreviewPointerEventArgs(PreviewPointerAction action, RegionPoint position) : EventArgs
{
    public PreviewPointerAction Action { get; } = action;

    public RegionPoint Position { get; } = position;
}

/// <summary>
/// Hosts the preview's Win32 child window. WPF can't draw over it (airspace), so the region overlay is drawn by the
/// preview renderer and the mouse is read here, from the child window's own messages.
/// </summary>
public sealed class PreviewHost : HwndHost
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CAPTURECHANGED = 0x0215;

    private PreviewChildWindow? _window;
    private PreviewRenderer? _renderer;
    private (string Path, WallpaperScaling Scaling, System.Drawing.Size Reference)? _image;
    private SnowSettings _snow = new();
    private RainSettings _rain = new();
    private GlassSettings _glass = new();
    private WaterSettings _water = new();
    private readonly Dictionary<EffectKind, RegionSet> _regions = [];
    private (IReadOnlyList<OverlayShape> Shapes, float Scale) _overlay = ([], 1f);
    private FrameRateMode _frameRate = FrameRateMode.Fps60;
    private bool _isPressed;
    private RegionPoint _lastPosition;

    public event EventHandler<PreviewPointerEventArgs>? PointerInput;

    public event EventHandler? PixelSizeChanged;

    /// <summary>The child window's size in device pixels, which is also the render size.</summary>
    public System.Drawing.Size PixelSize { get; private set; }

    /// <param name="path">The image file.</param>
    /// <param name="scaling">How it is scaled.</param>
    /// <param name="reference">The monitor the preview stands for, in pixels.</param>
    public void SetImage(string path, WallpaperScaling scaling, System.Drawing.Size reference)
    {
        _image = (path, scaling, reference);
        _renderer?.SetImage(path, scaling, reference);
    }

    public void SetSnow(SnowSettings settings)
    {
        _snow = settings;
        _renderer?.SetSnow(settings);
    }

    public void SetRain(RainSettings settings)
    {
        _rain = settings;
        _renderer?.SetRain(settings);
    }

    public void SetGlass(GlassSettings settings)
    {
        _glass = settings;
        _renderer?.SetGlass(settings);
    }

    public void SetWater(WaterSettings settings)
    {
        _water = settings;
        _renderer?.SetWater(settings);
    }

    public void SetRegions(EffectKind kind, RegionSet regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        _regions[kind] = regions;
        _renderer?.SetRegions(kind, regions);
    }

    public void SetOverlay(IReadOnlyList<OverlayShape> shapes, float scale)
    {
        _overlay = (shapes, scale);
        _renderer?.SetOverlay(shapes, scale);
    }

    public void SetFrameRate(FrameRateMode mode)
    {
        _frameRate = mode;
        _renderer?.SetFrameRate(mode);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _window = PreviewChildWindow.Create(hwndParent.Handle, 1, 1);
        _renderer = new PreviewRenderer(_window.Handle, new System.Drawing.Size(1, 1));
        _renderer.SetSnow(_snow);
        _renderer.SetRain(_rain);
        _renderer.SetGlass(_glass);
        _renderer.SetWater(_water);
        foreach (var (kind, regions) in _regions)
        {
            _renderer.SetRegions(kind, regions);
        }
        _renderer.SetOverlay(_overlay.Shapes, _overlay.Scale);
        _renderer.SetFrameRate(_frameRate);
        if (_image is { } image)
        {
            _renderer.SetImage(image.Path, image.Scaling, image.Reference);
        }
        return new HandleRef(this, _window.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _renderer?.Dispose();
        _renderer = null;
        _window?.Dispose();
        _window = null;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        var size = new System.Drawing.Size((int)Math.Round(rcBoundingBox.Width), (int)Math.Round(rcBoundingBox.Height));
        _renderer?.Resize(size);
        if (size != PixelSize)
        {
            PixelSize = size;
            PixelSizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_LBUTTONDOWN:
                _window?.CaptureMouse();
                _isPressed = true;
                Raise(PreviewPointerAction.Pressed, PositionOf(lParam));
                handled = true;
                return 0;
            case WM_MOUSEMOVE:
                Raise(PreviewPointerAction.Moved, PositionOf(lParam));
                handled = true;
                return 0;
            case WM_LBUTTONUP:
                if (_isPressed)
                {
                    _isPressed = false;
                    PreviewChildWindow.ReleaseMouse();
                    Raise(PreviewPointerAction.Released, PositionOf(lParam));
                }
                handled = true;
                return 0;
            case WM_CAPTURECHANGED:
                if (_isPressed)
                {
                    _isPressed = false;
                    Raise(PreviewPointerAction.Released, _lastPosition);
                }
                break;
            case WM_RBUTTONUP:
                Raise(PreviewPointerAction.SecondaryClicked, PositionOf(lParam));
                handled = true;
                return 0;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static RegionPoint PositionOf(nint lParam)
    {
        var value = (long)lParam;
        return new RegionPoint((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    private void Raise(PreviewPointerAction action, RegionPoint position)
    {
        _lastPosition = position;
        PointerInput?.Invoke(this, new PreviewPointerEventArgs(action, position));
    }
}
