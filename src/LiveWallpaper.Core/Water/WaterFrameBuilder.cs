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

    /// <summary>Multiplies a target pixel position to give flow-texture coordinates; the field's cells are FlowField.Downscale pixels wide.</summary>
    public Vector2 FlowUvPerPx;
    public Vector2 Padding;
}

public static class WaterFrameBuilder
{
    /// <param name="phase">The water phase in [0, 1).</param>
    /// <param name="settings">The water settings.</param>
    /// <param name="imageDestination">Where the decoded crop lands, in target pixels.</param>
    /// <param name="displayedImageHeightPx">The height of the whole displayed source image, in target pixels; it sets the mist reach.</param>
    /// <param name="target">The render target size, in pixels.</param>
    /// <param name="flowSize">The flow field size, in cells.</param>
    /// <param name="result">Receives the constants.</param>
    public static void Build(double phase, WaterSettings settings, RectangleF imageDestination, double displayedImageHeightPx, Size target, Size flowSize, ref WaterFrameConstants result)
    {
        result.ImageDest = new Vector4(imageDestination.X, imageDestination.Y, imageDestination.Width, imageDestination.Height);
        result.Resolution = new Vector2(target.Width, target.Height);
        result.Phase = (float)phase;
        result.Cycle = (float)WaterModel.CycleSeconds(settings.StretchPct);
        result.Foam = (float)(settings.FoamPct / 100);
        result.Scale = (float)(target.Height / (double)SnowModel.ReferenceHeightPx);
        result.Mist = (float)(settings.MistPct / 100);
        result.MistReach = (float)WaterModel.MistReachPx(settings.MistPct, displayedImageHeightPx);
        result.FlowUvPerPx = new Vector2(
            (float)(1.0 / (FlowField.Downscale * (double)Math.Max(1, flowSize.Width))),
            (float)(1.0 / (FlowField.Downscale * (double)Math.Max(1, flowSize.Height))));
    }
}
