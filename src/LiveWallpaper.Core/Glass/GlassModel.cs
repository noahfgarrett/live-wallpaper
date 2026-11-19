namespace LiveWallpaper.Core.Glass;

public static class GlassModel
{
    public const int MaxPanes = 16;

    public static double SizeMultiplier(double pct) => 0.6 + (0.8 * pct / 100);
}
