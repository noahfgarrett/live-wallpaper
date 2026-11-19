using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public sealed unsafe class WallpaperWindow : IDisposable
{
    public const string ClassName = "LiveWallpaper.WallpaperWindow";

    private const uint WM_ERASEBKGND = 0x0014;
    private static readonly Lock s_registerGate = new();
    private static bool s_registered;

    private WallpaperWindow(nint handle)
    {
        Handle = handle;
    }

    public nint Handle { get; }

    public static WallpaperWindow Create()
    {
        var instance = Kernel32.GetModuleHandle(null);
        EnsureClassRegistered(instance);
        var handle = User32.CreateWindowEx(
            (uint)(User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE | User32.WS_EX_NOPARENTNOTIFY),
            ClassName,
            "Live Wallpaper",
            unchecked((uint)(User32.WS_POPUP | User32.WS_CLIPSIBLINGS)),
            0, 0, 1, 1, 0, 0, instance, 0);
        if (handle == 0)
        {
            throw new InvalidOperationException($"Could not create the wallpaper window (error {Marshal.GetLastPInvokeError()}).");
        }
        return new WallpaperWindow(handle);
    }

    public void ShowWithoutActivating() => User32.ShowWindow(Handle, User32.SW_SHOWNA);

    public void Dispose()
    {
        if (User32.IsWindow(Handle))
        {
            User32.DestroyWindow(Handle);
        }
    }

    private static void EnsureClassRegistered(nint instance)
    {
        lock (s_registerGate)
        {
            if (s_registered)
            {
                return;
            }
            fixed (char* className = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = instance,
                    lpszClassName = className,
                };
                if (User32.RegisterClassEx(wc) == 0)
                {
                    throw new InvalidOperationException($"Could not register the wallpaper window class (error {Marshal.GetLastPInvokeError()}).");
                }
            }
            s_registered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam) =>
        msg == WM_ERASEBKGND ? 1 : User32.DefWindowProc(hwnd, msg, wParam, lParam);
}
