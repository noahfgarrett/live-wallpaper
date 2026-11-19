using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public sealed unsafe class RenderLoop : IDisposable
{
    private readonly nint _timer;
    private volatile bool _stopped;
    private volatile bool _frameRequested;

    public RenderLoop()
    {
        _timer = Kernel32.CreateWaitableTimerEx(0, null, Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Kernel32.TIMER_ALL_ACCESS);
        if (_timer == 0)
        {
            _timer = Kernel32.CreateWaitableTimerEx(0, null, 0, Kernel32.TIMER_ALL_ACCESS);
        }
        if (_timer == 0)
        {
            throw new InvalidOperationException($"Could not create the frame timer (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Run(Func<TimeSpan?> onFrame)
    {
        _stopped = false;
        Arm(onFrame());
        var timer = _timer;
        while (!_stopped)
        {
            var result = User32.MsgWaitForMultipleObjectsEx(1, &timer, User32.INFINITE, User32.QS_ALLINPUT, User32.MWMO_INPUTAVAILABLE);
            if (result == 1)
            {
                while (User32.PeekMessage(out var msg, 0, 0, 0, User32.PM_REMOVE))
                {
                    if (msg.message == User32.WM_QUIT)
                    {
                        _stopped = true;
                        break;
                    }
                    User32.TranslateMessage(msg);
                    User32.DispatchMessage(msg);
                }
                if (!_frameRequested)
                {
                    continue;
                }
            }
            if (_stopped)
            {
                break;
            }
            _frameRequested = false;
            Arm(onFrame());
        }
        Kernel32.CancelWaitableTimer(_timer);
    }

    public void RequestFrame()
    {
        _frameRequested = true;
        long immediately = -1;
        Kernel32.SetWaitableTimer(_timer, in immediately, 0, 0, 0, false);
    }

    public void Stop()
    {
        _stopped = true;
        long immediately = -1;
        Kernel32.SetWaitableTimer(_timer, in immediately, 0, 0, 0, false);
    }

    public void Dispose() => Kernel32.CloseHandle(_timer);

    private void Arm(TimeSpan? delay)
    {
        if (delay is not { } d)
        {
            Kernel32.CancelWaitableTimer(_timer);
            return;
        }
        var due = -Math.Max(1, d.Ticks);
        Kernel32.SetWaitableTimer(_timer, in due, 0, 0, 0, false);
    }
}
