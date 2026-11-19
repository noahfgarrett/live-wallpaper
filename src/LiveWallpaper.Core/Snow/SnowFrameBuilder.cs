using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace LiveWallpaper.Core.Snow;

[StructLayout(LayoutKind.Sequential)]
public struct SnowFrameConstants
{
    public Vector4 WindOffsets;
    public Vector2 Resolution;
    public float FallPhase;
    public float TimePhase;
    public float WindPxPerSec;
    public float SpeedMul;
    public float SizeMul;
    public float Margin;
    public float Scale;
    public float MotionBlur;
    public float Period;
    public float FlakeCount;
}

public static class SnowFrameBuilder
{
    public static void Build(
        double fallTime,
        double windOffsetPx,
        double windPxPerSec,
        double timeSec,
        double speedPct,
        double sizePct,
        bool motionBlur,
        Size target,
        ref SnowFrameConstants result)
    {
        var scale = target.Height / (double)SnowModel.ReferenceHeightPx;
        var margin = SnowModel.WrapMarginPx(scale);
        var spanX = target.Width + (2 * margin);
        var layers = SnowModel.Layers;

        result.Resolution = new Vector2(target.Width, target.Height);
        result.FallPhase = (float)Fract(fallTime / SnowModel.PeriodSeconds);
        result.TimePhase = (float)Fract(timeSec / SnowModel.PeriodSeconds);
        result.WindOffsets = new Vector4(
            (float)PositiveMod(windOffsetPx * layers[0].WindResponse, spanX),
            (float)PositiveMod(windOffsetPx * layers[1].WindResponse, spanX),
            (float)PositiveMod(windOffsetPx * layers[2].WindResponse, spanX),
            0);
        result.WindPxPerSec = (float)windPxPerSec;
        result.SpeedMul = (float)SnowModel.SpeedMultiplier(speedPct);
        result.SizeMul = (float)SnowModel.SizeMultiplier(sizePct);
        result.Margin = (float)margin;
        result.Scale = (float)scale;
        result.MotionBlur = motionBlur ? 1 : 0;
        result.Period = (float)SnowModel.PeriodSeconds;
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
