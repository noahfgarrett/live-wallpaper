namespace LiveWallpaper.Core.Models;

public sealed record WaterSettings(
    bool Enabled = false,
    double StretchPct = 50,
    double FoamPct = 30,
    double MistPct = 40)
{
    public bool IsValid => InRange(StretchPct) && InRange(FoamPct) && InRange(MistPct);

    private static bool InRange(double pct) => double.IsFinite(pct) && pct is >= 0 and <= 100;
}
