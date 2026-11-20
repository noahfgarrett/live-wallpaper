using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Rain;

[StructLayout(LayoutKind.Sequential)]
public struct RainFrameConstants
{
    public Vector4 WindOffsets;
    public Vector2 Resolution;
    public float FallPhase;
    public float WindPxPerSec;
    public float SpeedMul;
    public float Exposure;
    public float Margin;
    public float Scale;
    public float Opacity;
    public float Period;
    public float StreakCount;
    public float Padding;
}

public static class RainFrameBuilder
{
    public static void Build(
        double fallTime,
        double windOffsetPx,
        double windPxPerSec,
        RainSettings settings,
        Size target,
        ref RainFrameConstants result)
    {
        var scale = target.Height / (double)SnowModel.ReferenceHeightPx;
        var margin = RainModel.WrapMarginPx(scale);
        var spanX = target.Width + (2 * margin);
        var layers = RainModel.Layers;
        var megapixels = target.Width * (double)target.Height / 1_000_000;

        result.Resolution = new Vector2(target.Width, target.Height);
        result.FallPhase = (float)Fract(fallTime / RainModel.PeriodSeconds);
        result.WindOffsets = new Vector4(
            (float)PositiveMod(windOffsetPx * layers[0].WindResponse, spanX),
            (float)PositiveMod(windOffsetPx * layers[1].WindResponse, spanX),
            (float)PositiveMod(windOffsetPx * layers[2].WindResponse, spanX),
            0);
        result.WindPxPerSec = (float)windPxPerSec;
        result.SpeedMul = (float)RainModel.SpeedMultiplier(settings.SpeedPct);
        result.Exposure = (float)RainModel.ExposureSeconds(settings.LengthPct);
        result.Margin = (float)margin;
        result.Scale = (float)scale;
        result.Opacity = (float)RainModel.OpacityMultiplier(settings.BrightnessPct);
        result.Period = (float)RainModel.PeriodSeconds;
        result.StreakCount = RainModel.StreakCount(settings.DensityPct, megapixels);
        result.Padding = 0;
    }

    private static double Fract(double value)
    {
        var f = value - Math.Floor(value);
        return f >= 1 ? 0 : f;
    }

    private static double PositiveMod(double value, double modulus)
    {
        var m = value % modulus;
        return m < 0 ? m + modulus : m;
    }
}
