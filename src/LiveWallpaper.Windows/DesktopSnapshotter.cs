using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class DesktopSnapshotter
{
    public static DesktopSnapshot Capture()
    {
        var nodes = new List<WindowNode>();
        var topLevel = WindowEnumeration.TopLevel();
        for (var zOrder = 0; zOrder < topLevel.Count; zOrder++)
        {
            var hwnd = topLevel[zOrder];
            var className = User32.GetClassName(hwnd);
            var hostsIcons = User32.FindWindowEx(hwnd, 0, DesktopLayoutClassifier.DefViewClass, null) != 0;
            if (className is not (DesktopLayoutClassifier.ProgmanClass or DesktopLayoutClassifier.WorkerWClass) && !hostsIcons)
            {
                continue;
            }
            nodes.Add(Node(hwnd, className, 0, zOrder));
            if (className == DesktopLayoutClassifier.ProgmanClass || hostsIcons)
            {
                var children = WindowEnumeration.DirectChildren(hwnd);
                for (var i = 0; i < children.Count; i++)
                {
                    nodes.Add(Node(children[i], User32.GetClassName(children[i]), hwnd, i));
                }
            }
        }
        return new DesktopSnapshot(Environment.OSVersion.Version.ToString(), nodes);
    }

    private static WindowNode Node(nint hwnd, string className, nint parent, int zOrder)
    {
        User32.GetWindowRect(hwnd, out var rect);
        return new WindowNode(
            hwnd,
            className,
            parent,
            zOrder,
            User32.IsWindowVisible(hwnd),
            unchecked((uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE)),
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
    }
}
