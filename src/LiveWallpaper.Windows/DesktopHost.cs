using System.Drawing;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Geometry;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class DesktopHost
{
    private const uint SplitMessage = 0x052C;
    private const uint SplitTimeoutMs = 1000;
    private const int SplitPollAttempts = 10;
    private const int SplitPollIntervalMs = 100;

    public static DesktopAttachPlan Prepare()
    {
        var progman = User32.FindWindow(DesktopLayoutClassifier.ProgmanClass, null);
        if (progman == 0)
        {
            return new DesktopAttachPlan.Unsupported("No Progman window was found. Is Explorer running?");
        }

        var plan = DesktopLayoutClassifier.Classify(DesktopSnapshotter.Capture());
        if (plan is not DesktopAttachPlan.Unsupported)
        {
            Log.Info("desktop", $"event=already-split plan={plan}");
            return plan;
        }

        foreach (var (wParam, lParam) in new (nuint, nint)[] { (0xD, 0x1), (0, 0) })
        {
            User32.SendMessageTimeout(progman, SplitMessage, wParam, lParam, User32.SMTO_NORMAL, SplitTimeoutMs, out _);
            for (var attempt = 0; attempt < SplitPollAttempts; attempt++)
            {
                plan = DesktopLayoutClassifier.Classify(DesktopSnapshotter.Capture());
                if (plan is not DesktopAttachPlan.Unsupported)
                {
                    Log.Info("desktop", $"event=split wParam=0x{wParam:X} lParam=0x{lParam:X} plan={plan}");
                    return plan;
                }
                Thread.Sleep(SplitPollIntervalMs);
            }
        }

        Log.Info("desktop", $"event=split-failed plan={plan}");
        return plan;
    }

    public static void Attach(nint window, DesktopAttachPlan plan, Rectangle monitorBounds)
    {
        var (parent, insertAfter, isLayered) = plan switch
        {
            DesktopAttachPlan.LegacyWorkerW legacy => ((nint)legacy.WorkerW, (nint)0, false),
            DesktopAttachPlan.ProgmanChild raised => ((nint)raised.Progman, (nint)raised.DefView, true),
            DesktopAttachPlan.Unsupported unsupported => throw new InvalidOperationException(unsupported.Reason),
            _ => throw new InvalidOperationException("Unknown desktop attach plan."),
        };

        var style = (long)User32.GetWindowLongPtr(window, User32.GWL_STYLE);
        User32.SetWindowLongPtr(window, User32.GWL_STYLE, (nint)((style & ~User32.WS_POPUP) | User32.WS_CHILD));

        if (isLayered)
        {
            var exStyle = (long)User32.GetWindowLongPtr(window, User32.GWL_EXSTYLE);
            User32.SetWindowLongPtr(window, User32.GWL_EXSTYLE, (nint)(exStyle | User32.WS_EX_LAYERED));
            User32.SetLayeredWindowAttributes(window, 0, 255, User32.LWA_ALPHA);
        }

        User32.SetParent(window, parent);
        if (User32.GetParent(window) != parent)
        {
            throw new InvalidOperationException("Windows refused to place the wallpaper window behind the desktop icons.");
        }

        var origin = new POINT();
        User32.ClientToScreen(parent, ref origin);
        var rect = DesktopCoordinates.ScreenToParentClient(monitorBounds, new Point(origin.X, origin.Y));
        var flags = User32.SWP_NOACTIVATE | (isLayered ? 0 : User32.SWP_NOZORDER);
        User32.SetWindowPos(window, insertAfter, rect.X, rect.Y, rect.Width, rect.Height, flags);
        Log.Info("desktop", $"event=attached window=0x{window:X} parent=0x{parent:X} rect={rect.X},{rect.Y},{rect.Width}x{rect.Height} layered={isLayered}");
    }

    public static bool IsStillAttached(nint window, DesktopAttachPlan plan)
    {
        if (!User32.IsWindow(window))
        {
            return false;
        }
        switch (plan)
        {
            case DesktopAttachPlan.LegacyWorkerW legacy:
                return User32.GetParent(window) == (nint)legacy.WorkerW;
            case DesktopAttachPlan.ProgmanChild raised:
                if (User32.GetParent(window) != (nint)raised.Progman || User32.GetParent((nint)raised.DefView) != (nint)raised.Progman)
                {
                    return false;
                }
                for (var h = User32.GetWindow((nint)raised.DefView, User32.GW_HWNDNEXT); h != 0; h = User32.GetWindow(h, User32.GW_HWNDNEXT))
                {
                    if (h == window)
                    {
                        return true;
                    }
                    if (h == (nint)raised.WorkerW)
                    {
                        return false;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    public static bool UsesLayeredWindow(DesktopAttachPlan plan) => plan is DesktopAttachPlan.ProgmanChild;
}
