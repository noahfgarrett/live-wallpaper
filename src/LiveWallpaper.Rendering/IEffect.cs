using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public readonly record struct FrameTime(double DeltaSeconds, double TotalSeconds);

/// <summary>What an effect can see while it renders.</summary>
/// <param name="Mask">Its region mask, sampled across the whole target; one white pixel when it has no regions.</param>
/// <param name="Image">The background image, possibly a crop of the source.</param>
/// <param name="ImageDestination">Where <paramref name="Image"/> is drawn, in target pixels.</param>
/// <param name="Frame">A copy of the frame so far with a full mip chain; null unless the effect needs it.</param>
public readonly record struct EffectInputs(ID3D11ShaderResourceView Mask, ID3D11ShaderResourceView Image, RectangleF ImageDestination, ID3D11ShaderResourceView? Frame);

public interface IEffect : IDisposable
{
    EffectKind Kind { get; }

    int ActiveParticles { get; }

    bool IsAnimated { get; }

    bool NeedsFrameCopy { get; }

    /// <summary>Called by the scene when this effect's regions or the target change, and once before it first draws.</summary>
    void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels);

    void Update(in FrameTime time, Size targetPixels);

    void Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs);
}
