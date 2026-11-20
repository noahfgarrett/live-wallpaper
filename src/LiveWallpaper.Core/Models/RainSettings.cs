namespace LiveWallpaper.Core.Models;

public sealed record RainSettings(
    bool Enabled = false,
    double DensityPct = 50,
    double SpeedPct = 50,
    double WindPct = 20,
    double LengthPct = 50,
    double BrightnessPct = 50)
{
    public bool IsValid =>
        InRange(DensityPct) && InRange(SpeedPct) && InRange(WindPct) && InRange(LengthPct) && InRange(BrightnessPct);

    private static bool InRange(double pct) => double.IsFinite(pct) && pct is >= 0 and <= 100;
}
