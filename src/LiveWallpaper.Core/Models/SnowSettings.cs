namespace LiveWallpaper.Core.Models;

public sealed record SnowSettings(
    bool Enabled = true,
    double DensityPct = 50,
    double SpeedPct = 50,
    double WindPct = 30,
    double SizePct = 50,
    bool MotionBlur = false)
{
    public bool IsValid =>
        InRange(DensityPct) && InRange(SpeedPct) && InRange(WindPct) && InRange(SizePct);

    private static bool InRange(double pct) => double.IsFinite(pct) && pct is >= 0 and <= 100;
}
