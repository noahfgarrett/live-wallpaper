using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Rain;

public sealed record RainLayer(
    string Name,
    float Share,
    float WidthMin,
    float WidthMax,
    float FallMin,
    float FallMax,
    float OpacityMin,
    float OpacityMax,
    float WindResponse);

public static class RainModel
{
    public const int MaxStreaks = 9000;
    public const int StreaksPerMegapixel = 1400;
    public const double WindBaseAtFull = 520;
    public const double PeriodSeconds = SnowModel.PeriodSeconds;

    private const double LargestFallPxPerSec = 2500;

    private static readonly RainLayer[] s_layers =
    [
        new("far", 0.62f, 0.9f, 1.2f, 900, 1200, 0.10f, 0.20f, 0.7f),
        new("mid", 0.30f, 1.2f, 1.7f, 1300, 1700, 0.16f, 0.28f, 0.9f),
        new("near", 0.08f, 2.0f, 3.0f, 1900, 2500, 0.10f, 0.20f, 1.1f),
    ];

    public static IReadOnlyList<RainLayer> Layers => s_layers;

    public static int StreakCount(double densityPct, double megapixels)
    {
        var d = SnowModel.ClampPct(densityPct) / 100;
        return (int)Math.Min(MaxStreaks, Math.Round(StreaksPerMegapixel * Math.Pow(d, 1.4) * Math.Max(0, megapixels), MidpointRounding.AwayFromZero));
    }

    public static double SpeedMultiplier(double speedPct) => 0.5 + (SnowModel.ClampPct(speedPct) / 100);

    public static double ExposureSeconds(double lengthPct) => 0.008 + (0.042 * (SnowModel.ClampPct(lengthPct) / 100));

    public static double OpacityMultiplier(double brightnessPct) => 0.4 + (1.6 * (SnowModel.ClampPct(brightnessPct) / 100));

    public static double WrapMarginPx(double scale) =>
        ((LargestFallPxPerSec * SpeedMultiplier(100) * ExposureSeconds(100) * 1.2) + 9 + 40) * scale;
}
