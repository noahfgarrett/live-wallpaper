using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public sealed unsafe class PreviewChildWindow : IDisposable
{
    public const string ClassName = "LiveWallpaper.PreviewWindow";

    private const uint WS_VISIBLE = 0x10000000;
    private const uint WM_ERASEBKGND = 0x0014;
    private static readonly Lock s_registerGate = new();
    private static bool s_registered;

    private PreviewChildWindow(nint handle)
    {
        Handle = handle;
    }

    public nint Handle { get; }

    public static PreviewChildWindow Create(nint parent, int width, int height)
    {
        var instance = Kernel32.GetModuleHandle(null);
        lock (s_registerGate)
        {
            if (!s_registered)
            {
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
                        throw new InvalidOperationException($"Could not register the preview window class (error {Marshal.GetLastPInvokeError()}).");
                    }
                }
                s_registered = true;
            }
        }
        var handle = User32.CreateWindowEx(0, ClassName, "Preview",
            (uint)(User32.WS_CHILD | User32.WS_CLIPSIBLINGS) | WS_VISIBLE,
            0, 0, Math.Max(1, width), Math.Max(1, height), parent, 0, instance, 0);
        if (handle == 0)
        {
            throw new InvalidOperationException($"Could not create the preview window (error {Marshal.GetLastPInvokeError()}).");
        }
        return new PreviewChildWindow(handle);
    }

    public void Dispose()
    {
        if (User32.IsWindow(Handle))
        {
            User32.DestroyWindow(Handle);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam) =>
        msg == WM_ERASEBKGND ? 1 : User32.DefWindowProc(hwnd, msg, wParam, lParam);
}
