using System.Drawing;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public sealed class WallpaperScene : IDisposable
{
    private readonly ImageQuadRenderer _background;
    private readonly ID3D11ShaderResourceView _image;
    private readonly RectangleF _destination;
    private readonly List<IEffect> _effects = [];
    private readonly RegionMaskTexture _mask;
    private RegionSet _regions = RegionSet.Empty;
    private Size _maskTarget = Size.Empty;
    private bool _maskStale = true;

    /// <param name="gpu">The device that owns the mask texture.</param>
    /// <param name="background">Draws the image.</param>
    /// <param name="image">The decoded image, possibly a crop of the source.</param>
    /// <param name="destination">Where <paramref name="image"/> is drawn, in target pixels.</param>
    /// <param name="imageBounds">Where the whole source image lands, in target pixels; regions are mapped through it.</param>
    public WallpaperScene(GpuDevice gpu, ImageQuadRenderer background, ID3D11ShaderResourceView image, RectangleF destination, RectangleF imageBounds)
    {
        _background = background;
        _image = image;
        _destination = destination;
        ImageBounds = imageBounds;
        _mask = new RegionMaskTexture(gpu);
    }

    public IList<IEffect> Effects => _effects;

    public RectangleF ImageBounds { get; }

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

    /// <summary>Sets where the effects show. Call on the render thread; the mask is rebuilt on the next update or render.</summary>
    public void SetRegions(RegionSet regions)
    {
        if (ReferenceEquals(regions, _regions))
        {
            return;
        }
        if (!regions.Equals(_regions))
        {
            _maskStale = true;
        }
        _regions = regions;
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        EnsureMask(targetPixels);
        foreach (var effect in _effects)
        {
            effect.Update(time, targetPixels);
        }
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels)
    {
        EnsureMask(targetPixels);
        _background.Draw(target, targetPixels, _image, _destination);
        foreach (var effect in _effects)
        {
            effect.Render(target, targetPixels, _mask.View);
        }
    }

    private void EnsureMask(Size targetPixels)
    {
        if (!_maskStale && targetPixels == _maskTarget)
        {
            return;
        }
        _mask.Update(RegionMask.Build(_regions, ImageBounds, targetPixels));
        _maskTarget = targetPixels;
        _maskStale = false;
    }

    public void Dispose()
    {
        foreach (var effect in _effects)
        {
            effect.Dispose();
        }
        _effects.Clear();
        _mask.Dispose();
        _image.Dispose();
    }
}
