using System.Runtime.InteropServices;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public enum MessageWindowEvent
{
    ExplorerRestarted,
    DisplayChanged,
    ReloadRequested,
    CloseRequested,
    AttachmentCheckDue,
}

public sealed unsafe class MessageWindow : IDisposable
{
    public const string ClassName = "LiveWallpaper.Engine.MessageWindow";

    private const nuint DebounceTimerId = 1;
    private const nuint AttachmentTimerId = 2;
    private const uint DebounceMs = 500;

    private static readonly uint s_taskbarCreated = User32.RegisterWindowMessage("TaskbarCreated");
    private static readonly uint s_reload = User32.RegisterWindowMessage("LiveWallpaper.Reload");
    private static readonly Lock s_registerGate = new();
    private static bool s_registered;
    private static MessageWindow? s_current;

    private readonly Action<MessageWindowEvent> _onEvent;
    private MessageWindowEvent? _pending;

    public MessageWindow(Action<MessageWindowEvent> onEvent)
    {
        _onEvent = onEvent;
        var instance = Kernel32.GetModuleHandle(null);
        EnsureClassRegistered(instance);
        s_current = this;
        Handle = User32.CreateWindowEx(
            (uint)User32.WS_EX_TOOLWINDOW,
            ClassName,
            "Live Wallpaper Engine",
            unchecked((uint)User32.WS_POPUP),
            0, 0, 0, 0, 0, 0, instance, 0);
        if (Handle == 0)
        {
            s_current = null;
            throw new InvalidOperationException($"Could not create the engine message window (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public nint Handle { get; }

    public static nint FindRunning() => User32.FindWindow(ClassName, null);

    public static void PostReload(nint window) => User32.PostMessage(window, s_reload, 0, 0);

    public static void PostClose(nint window) => User32.PostMessage(window, User32.WM_CLOSE, 0, 0);

    public static void Quit() => User32.PostQuitMessage(0);

    public void StartAttachmentChecks(uint intervalMs) => User32.SetTimer(Handle, AttachmentTimerId, intervalMs, 0);

    public void RunMessageLoop()
    {
        while (User32.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(msg);
            User32.DispatchMessage(msg);
        }
    }

    public void Dispose()
    {
        if (User32.IsWindow(Handle))
        {
            User32.KillTimer(Handle, DebounceTimerId);
            User32.KillTimer(Handle, AttachmentTimerId);
            User32.DestroyWindow(Handle);
        }
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    private void Debounce(MessageWindowEvent e)
    {
        if (_pending != MessageWindowEvent.ExplorerRestarted)
        {
            _pending = e;
        }
        User32.SetTimer(Handle, DebounceTimerId, DebounceMs, 0);
    }

    private void Raise(MessageWindowEvent e)
    {
        try
        {
            _onEvent(e);
        }
        catch (Exception ex)
        {
            Log.Error("engine", $"event=handler-failed message={e}", ex);
        }
    }

    private nint OnMessage(uint msg, nuint wParam, nint lParam, nint hwnd)
    {
        if (msg == s_taskbarCreated)
        {
            Debounce(MessageWindowEvent.ExplorerRestarted);
            return 0;
        }
        if (msg == s_reload)
        {
            Raise(MessageWindowEvent.ReloadRequested);
            return 0;
        }
        switch (msg)
        {
            case User32.WM_DISPLAYCHANGE:
            case User32.WM_DPICHANGED:
                Debounce(MessageWindowEvent.DisplayChanged);
                return 0;
            case User32.WM_SETTINGCHANGE when wParam == User32.SPI_SETWORKAREA:
                Debounce(MessageWindowEvent.DisplayChanged);
                return 0;
            case User32.WM_TIMER when wParam == DebounceTimerId:
                User32.KillTimer(hwnd, DebounceTimerId);
                var pending = _pending;
                _pending = null;
                if (pending is { } e)
                {
                    Raise(e);
                }
                return 0;
            case User32.WM_TIMER when wParam == AttachmentTimerId:
                Raise(MessageWindowEvent.AttachmentCheckDue);
                return 0;
            case User32.WM_CLOSE:
                Raise(MessageWindowEvent.CloseRequested);
                return 0;
            default:
                return User32.DefWindowProc(hwnd, msg, wParam, lParam);
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
                    throw new InvalidOperationException($"Could not register the engine window class (error {Marshal.GetLastPInvokeError()}).");
                }
            }
            s_registered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var current = s_current;
        return current is not null && current.Handle == hwnd
            ? current.OnMessage(msg, wParam, lParam, hwnd)
            : User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
