namespace LiveWallpaper.Core.Snow;

public sealed record SnowLayer(
    string Name,
    float Share,
    float SizeMin,
    float SizeMax,
    float FallMin,
    float FallMax,
    float OpacityMin,
    float OpacityMax,
    float Softness,
    float WindResponse,
    float SwayMin,
    float SwayMax,
    float SwayHzMin,
    float SwayHzMax);

public static class SnowModel
{
    public const float ReferenceHeightPx = 1080;
    public const int MaxFlakes = 12000;
    public const int MaxFlakesPerMegapixel = 1500;
    public const double PeriodSeconds = 3600;

    private static readonly SnowLayer[] s_layers =
    [
        new("far", 0.55f, 1.2f, 2.4f, 30, 55, 0.35f, 0.60f, 0.35f, 0.5f, 4, 10, 0.10f, 0.25f),
        new("mid", 0.35f, 2.5f, 4.5f, 60, 95, 0.60f, 0.85f, 0.50f, 0.8f, 8, 18, 0.15f, 0.35f),
        new("near", 0.10f, 6.0f, 12.0f, 110, 170, 0.35f, 0.60f, 0.90f, 1.2f, 14, 30, 0.20f, 0.45f),
    ];

    private static readonly double s_largestSizePx = s_layers.Max(l => (double)l.SizeMax);

    public static IReadOnlyList<SnowLayer> Layers => s_layers;

    public static int FlakeCount(double densityPct, double megapixels)
    {
        var d = ClampPct(densityPct) / 100;
        return (int)Math.Min(MaxFlakes, Math.Round(MaxFlakesPerMegapixel * Math.Pow(d, 1.5) * Math.Max(0, megapixels), MidpointRounding.AwayFromZero));
    }

    public static double SpeedMultiplier(double speedPct) => 0.25 + (1.5 * (ClampPct(speedPct) / 100));

    public static double SizeMultiplier(double sizePct) => 0.5 + (ClampPct(sizePct) / 100);

    public static double WrapMarginPx(double scale) => ((s_largestSizePx * SizeMultiplier(100) * 1.5) + 40) * scale;

    internal static double ClampPct(double pct) => double.IsFinite(pct) ? Math.Clamp(pct, 0, 100) : 0;
}
