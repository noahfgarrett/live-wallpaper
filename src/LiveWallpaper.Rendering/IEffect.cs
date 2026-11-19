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

    void Render(ID3D11RenderTargetView target, Size targetPixels);
}
