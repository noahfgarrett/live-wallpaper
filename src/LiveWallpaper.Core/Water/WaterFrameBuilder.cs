using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Water;

[StructLayout(LayoutKind.Sequential)]
public struct WaterFrameConstants
{
    /// <summary>x, y, width, height of the displayed image in target pixels.</summary>
    public Vector4 ImageDest;
    public Vector2 Resolution;
    public float Phase;
    public float Cycle;
    public float Foam;
    public float Scale;
    public float Mist;
    public float MistReach;
}

public static class WaterFrameBuilder
{
    public static void Build(double phase, WaterSettings settings, RectangleF imageDestination, Size target, ref WaterFrameConstants result)
    {
        result.ImageDest = new Vector4(imageDestination.X, imageDestination.Y, imageDestination.Width, imageDestination.Height);
        result.Resolution = new Vector2(target.Width, target.Height);
        result.Phase = (float)phase;
        result.Cycle = (float)WaterModel.CycleSeconds(settings.StretchPct);
        result.Foam = (float)(settings.FoamPct / 100);
        result.Scale = (float)(target.Height / (double)SnowModel.ReferenceHeightPx);
        result.Mist = (float)(settings.MistPct / 100);
        result.MistReach = (float)WaterModel.MistReachPx(settings.MistPct, imageDestination.Height);
    }
}
