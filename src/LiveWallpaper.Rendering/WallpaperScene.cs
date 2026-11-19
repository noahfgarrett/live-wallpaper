using System.Drawing;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public sealed class WallpaperScene : IDisposable
{
    private readonly ImageQuadRenderer _background;
    private readonly ID3D11ShaderResourceView _image;
    private readonly RectangleF _destination;
    private readonly List<IEffect> _effects = [];

    public WallpaperScene(ImageQuadRenderer background, ID3D11ShaderResourceView image, RectangleF destination)
    {
        _background = background;
        _image = image;
        _destination = destination;
    }

    public IList<IEffect> Effects => _effects;

    public bool IsAnimated
    {
        get
        {
            foreach (var effect in _effects)
            {
                if (effect.IsAnimated)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public int ActiveParticles
    {
        get
        {
            var total = 0;
            foreach (var effect in _effects)
            {
                total += effect.ActiveParticles;
            }
            return total;
        }
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        foreach (var effect in _effects)
        {
            effect.Update(time, targetPixels);
        }
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels)
    {
        _background.Draw(target, targetPixels, _image, _destination);
        foreach (var effect in _effects)
        {
            effect.Render(target, targetPixels);
        }
    }

    public void Dispose()
    {
        foreach (var effect in _effects)
        {
            effect.Dispose();
        }
        _effects.Clear();
        _image.Dispose();
    }
}
