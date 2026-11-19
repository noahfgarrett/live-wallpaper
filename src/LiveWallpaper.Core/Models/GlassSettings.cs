namespace LiveWallpaper.Core.Models;

public sealed record GlassSettings(
    bool Enabled = false,
    double DropsPct = 50,
    double DropSizePct = 50,
    double SlidingPct = 40,
    double FogPct = 30)
{
    public bool IsValid =>
        InRange(DropsPct) && InRange(DropSizePct) && InRange(SlidingPct) && InRange(FogPct);

    private static bool InRange(double pct) => double.IsFinite(pct) && pct is >= 0 and <= 100;
}
