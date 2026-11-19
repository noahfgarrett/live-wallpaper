using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class DesktopSnapshotter
{
    public static DesktopSnapshot Capture()
    {
        var nodes = new List<WindowNode>();
        var zOrder = 0;
        for (var hwnd = User32.GetWindow(User32.GetDesktopWindow(), User32.GW_CHILD); hwnd != 0; hwnd = User32.GetWindow(hwnd, User32.GW_HWNDNEXT))
        {
            var className = User32.GetClassName(hwnd);
            var hostsIcons = User32.FindWindowEx(hwnd, 0, DesktopLayoutClassifier.DefViewClass, null) != 0;
            if (className is DesktopLayoutClassifier.ProgmanClass or DesktopLayoutClassifier.WorkerWClass || hostsIcons)
            {
                nodes.Add(Node(hwnd, className, 0, zOrder));
                if (className == DesktopLayoutClassifier.ProgmanClass || hostsIcons)
                {
                    AddChildren(nodes, hwnd);
                }
            }
            zOrder++;
        }
        return new DesktopSnapshot(Environment.OSVersion.Version.ToString(), nodes);
    }

    private static void AddChildren(List<WindowNode> nodes, nint parent)
    {
        var index = 0;
        for (var child = User32.GetWindow(parent, User32.GW_CHILD); child != 0; child = User32.GetWindow(child, User32.GW_HWNDNEXT))
        {
            nodes.Add(Node(child, User32.GetClassName(child), parent, index++));
        }
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
