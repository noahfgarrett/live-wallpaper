namespace LiveWallpaper.Core.Models;

public sealed record WaterSettings(
    bool Enabled = false,
    double StretchPct = 50,
    double FoamPct = 30)
{
    public bool IsValid => InRange(StretchPct) && InRange(FoamPct);

    private static bool InRange(double pct) => double.IsFinite(pct) && pct is >= 0 and <= 100;
}
