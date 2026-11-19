namespace LiveWallpaper.Core.Desktop;

public static class DesktopLayoutClassifier
{
    public const string ProgmanClass = "Progman";
    public const string WorkerWClass = "WorkerW";
    public const string DefViewClass = "SHELLDLL_DefView";

    public static DesktopAttachPlan Classify(DesktopSnapshot snapshot)
    {
        var windows = snapshot.Windows;
        var progman = windows.FirstOrDefault(w => w.ParentHandle == 0 && w.ClassName == ProgmanClass);
        if (progman is null)
        {
            return new DesktopAttachPlan.Unsupported("No top-level Progman window was found.");
        }

        var progmanChildren = ChildrenOf(windows, progman.Handle);
        var defViewInProgman = progmanChildren.FirstOrDefault(w => w.ClassName == DefViewClass);
        if (defViewInProgman is not null)
        {
            var workerW = progmanChildren
                .Where(w => w.ClassName == WorkerWClass)
                .OrderByDescending(w => w.Area)
                .FirstOrDefault();
            return workerW is null
                ? new DesktopAttachPlan.Unsupported(DesktopAttachPlan.NotSplitReason)
                : new DesktopAttachPlan.ProgmanChild(progman.Handle, defViewInProgman.Handle, workerW.Handle);
        }

        var topLevel = windows.Where(w => w.ParentHandle == 0).OrderBy(w => w.ZOrder).ToList();
        var host = topLevel.FirstOrDefault(w => ChildrenOf(windows, w.Handle).Any(c => c.ClassName == DefViewClass));
        if (host is null)
        {
            return new DesktopAttachPlan.Unsupported("The desktop icon view (SHELLDLL_DefView) was not found.");
        }

        var wallpaperLayer = topLevel.FirstOrDefault(w => w.ZOrder > host.ZOrder && w.ClassName == WorkerWClass);
        return wallpaperLayer is null
            ? new DesktopAttachPlan.Unsupported("No WorkerW was found behind the window that hosts the icons.")
            : new DesktopAttachPlan.LegacyWorkerW(wallpaperLayer.Handle);
    }

    private static List<WindowNode> ChildrenOf(IReadOnlyList<WindowNode> windows, long parent) =>
        windows.Where(w => w.ParentHandle == parent).OrderBy(w => w.ZOrder).ToList();
}
