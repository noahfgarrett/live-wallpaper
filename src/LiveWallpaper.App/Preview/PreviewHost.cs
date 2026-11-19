using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Preview;

public sealed class PreviewHost : HwndHost
{
    private PreviewChildWindow? _window;
    private PreviewRenderer? _renderer;
    private (string Path, WallpaperScaling Scaling)? _image;
    private SnowSettings _snow = new();
    private FrameRateMode _frameRate = FrameRateMode.Fps60;

    public void SetImage(string path, WallpaperScaling scaling)
    {
        _image = (path, scaling);
        _renderer?.SetImage(path, scaling);
    }

    public void SetSnow(SnowSettings settings)
    {
        _snow = settings;
        _renderer?.SetSnow(settings);
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
        _renderer.SetFrameRate(_frameRate);
        if (_image is { } image)
        {
            _renderer.SetImage(image.Path, image.Scaling);
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
        _renderer?.Resize(new System.Drawing.Size((int)Math.Round(rcBoundingBox.Width), (int)Math.Round(rcBoundingBox.Height)));
    }
}
