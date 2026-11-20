using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LiveWallpaper.Core.Geometry;
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

/// <summary>A request to zoom by <see cref="Factor"/> around <see cref="ViewPoint"/>, in the preview's client pixels.</summary>
public sealed class PreviewZoomEventArgs(System.Drawing.PointF viewPoint, double factor) : EventArgs
{
    public System.Drawing.PointF ViewPoint { get; } = viewPoint;

    public double Factor { get; } = factor;
}

/// <summary>A request to move the picture by <see cref="Delta"/> client pixels, the way the mouse moved.</summary>
public sealed class PreviewPanEventArgs(System.Drawing.SizeF delta) : EventArgs
{
    public System.Drawing.SizeF Delta { get; } = delta;
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
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_CAPTURECHANGED = 0x0215;
    private const int WheelNotch = 120;
    private const int PanThreshold = 4;

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
    private PreviewCamera? _camera;
    private Drag _drag = Drag.None;
    private RegionPoint _lastPosition;
    private RegionPoint _dragStart;
    private RegionPoint _panLast;

    /// <summary>What the held mouse button is doing; one button drives the preview at a time, and only its release ends it.</summary>
    private enum Drag
    {
        None,
        Edit,
        RightPressed,
        LeftPan,
        RightPan,
        MiddlePan,
    }

    public event EventHandler<PreviewPointerEventArgs>? PointerInput;

    public event EventHandler<PreviewZoomEventArgs>? ZoomRequested;

    public event EventHandler<PreviewPanEventArgs>? PanRequested;

    public event EventHandler? PixelSizeChanged;

    /// <summary>The child window's size in device pixels: the view the camera maps the desktop-sized scene into.</summary>
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

    public void SetCamera(PreviewCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _camera = camera;
        _renderer?.SetCamera(camera);
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
        if (_camera is not null)
        {
            _renderer.SetCamera(_camera);
        }
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
                if (_drag == Drag.None)
                {
                    if (PreviewChildWindow.IsSpaceDown())
                    {
                        BeginDrag(Drag.LeftPan, PositionOf(lParam));
                    }
                    else
                    {
                        BeginDrag(Drag.Edit, PositionOf(lParam));
                        Raise(PreviewPointerAction.Pressed, PositionOf(lParam));
                    }
                }
                handled = true;
                return 0;
            case WM_RBUTTONDOWN:
                if (_drag == Drag.None)
                {
                    BeginDrag(Drag.RightPressed, PositionOf(lParam));
                }
                handled = true;
                return 0;
            case WM_MBUTTONDOWN:
                if (_drag == Drag.None)
                {
                    BeginDrag(Drag.MiddlePan, PositionOf(lParam));
                }
                handled = true;
                return 0;
            case WM_MOUSEMOVE:
                OnMouseMove(PositionOf(lParam));
                handled = true;
                return 0;
            case WM_LBUTTONUP:
                if (_drag == Drag.Edit)
                {
                    EndDrag();
                    Raise(PreviewPointerAction.Released, PositionOf(lParam));
                }
                else if (_drag == Drag.LeftPan)
                {
                    EndDrag();
                }
                handled = true;
                return 0;
            case WM_RBUTTONUP:
                if (_drag == Drag.RightPressed)
                {
                    EndDrag();
                    Raise(PreviewPointerAction.SecondaryClicked, PositionOf(lParam));
                }
                else if (_drag == Drag.RightPan)
                {
                    EndDrag();
                }
                handled = true;
                return 0;
            case WM_MBUTTONUP:
                if (_drag == Drag.MiddlePan)
                {
                    EndDrag();
                }
                handled = true;
                return 0;
            case WM_MOUSEWHEEL:
                OnMouseWheel(wParam, lParam);
                handled = true;
                return 0;
            case WM_CAPTURECHANGED:
                // Another window took the mouse: whatever the button was doing ends here.
                var lost = _drag;
                _drag = Drag.None;
                if (lost == Drag.Edit)
                {
                    Raise(PreviewPointerAction.Released, _lastPosition);
                }
                break;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void BeginDrag(Drag drag, RegionPoint position)
    {
        _window?.CaptureMouse();
        _drag = drag;
        _dragStart = position;
        _panLast = position;
    }

    /// <summary>Clears the drag before releasing the mouse, so the WM_CAPTURECHANGED that the release sends finds nothing to end.</summary>
    private void EndDrag()
    {
        _drag = Drag.None;
        PreviewChildWindow.ReleaseMouse();
    }

    private void OnMouseMove(RegionPoint position)
    {
        if (_drag == Drag.RightPressed
            && (Math.Abs(position.X - _dragStart.X) > PanThreshold || Math.Abs(position.Y - _dragStart.Y) > PanThreshold))
        {
            _drag = Drag.RightPan;
        }
        if (_drag is Drag.LeftPan or Drag.RightPan or Drag.MiddlePan)
        {
            var delta = new System.Drawing.SizeF((float)(position.X - _panLast.X), (float)(position.Y - _panLast.Y));
            _panLast = position;
            PanRequested?.Invoke(this, new PreviewPanEventArgs(delta));
            return;
        }
        Raise(PreviewPointerAction.Moved, position);
    }

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        var delta = (short)(((long)wParam >> 16) & 0xFFFF);
        if (delta == 0 || _window is null)
        {
            return;
        }
        // Unlike the other mouse messages, the wheel's position is in screen coordinates.
        var value = (long)lParam;
        var screen = new System.Drawing.Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        if (_window.ScreenToClient(screen) is not { } client)
        {
            return;
        }
        var factor = Math.Pow(PreviewCamera.StepFactor, delta / (double)WheelNotch);
        ZoomRequested?.Invoke(this, new PreviewZoomEventArgs(new System.Drawing.PointF(client.X, client.Y), factor));
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
