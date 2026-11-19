using System.Drawing;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public readonly record struct FrameTime(double DeltaSeconds, double TotalSeconds);

public interface IEffect : IDisposable
{
    string Id { get; }

    int ActiveParticles { get; }

    bool IsAnimated { get; }

    void Update(in FrameTime time, Size targetPixels);

    /// <param name="target">The render target.</param>
    /// <param name="targetPixels">Its size in pixels.</param>
    /// <param name="mask">The region mask, sampled across the whole target; one white pixel when there are no regions.</param>
    void Render(ID3D11RenderTargetView target, Size targetPixels, ID3D11ShaderResourceView mask);
}
