using System.Text.Json.Serialization;

namespace LiveWallpaper.Core.Desktop;

public sealed record WindowNode(
    long Handle,
    string ClassName,
    long ParentHandle,
    int ZOrder,
    bool IsVisible,
    uint ExStyle,
    int X,
    int Y,
    int Width,
    int Height)
{
    [JsonIgnore]
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);
}

public sealed record DesktopSnapshot(string OsVersion, IReadOnlyList<WindowNode> Windows);

public abstract record DesktopAttachPlan
{
    public const string NotSplitReason = "The desktop has not been split: Progman hosts the icons but no WorkerW exists yet.";

    private DesktopAttachPlan()
    {
    }

    public sealed record LegacyWorkerW(long WorkerW) : DesktopAttachPlan;

    public sealed record ProgmanChild(long Progman, long DefView, long WorkerW) : DesktopAttachPlan;

    public sealed record Unsupported(string Reason) : DesktopAttachPlan;
}
