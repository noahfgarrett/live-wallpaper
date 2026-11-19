using System.Drawing;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public sealed class WallpaperScene : IDisposable
{
    private static readonly EffectKind[] s_drawSequence = [.. Enum.GetValues<EffectKind>().OrderBy(DrawOrder)];

    private readonly GpuDevice _gpu;
    private readonly ImageQuadRenderer _background;
    private readonly ID3D11ShaderResourceView _image;
    private readonly RectangleF _destination;
    private readonly List<IEffect> _effects = [];
    private readonly Dictionary<EffectKind, EffectSlot> _slots = [];
    private readonly HashSet<IEffect> _placed = [];
    private readonly FrameCopy _frameCopy;

    /// <param name="gpu">The device that owns the mask textures and the frame copy.</param>
    /// <param name="background">Draws the image.</param>
    /// <param name="image">The decoded image, possibly a crop of the source.</param>
    /// <param name="destination">Where <paramref name="image"/> is drawn, in target pixels.</param>
    /// <param name="imageBounds">Where the whole source image lands, in target pixels; regions are mapped through it.</param>
    public WallpaperScene(GpuDevice gpu, ImageQuadRenderer background, ID3D11ShaderResourceView image, RectangleF destination, RectangleF imageBounds)
    {
        _gpu = gpu;
        _background = background;
        _image = image;
        _destination = destination;
        ImageBounds = imageBounds;
        _frameCopy = new FrameCopy(gpu);
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

    /// <summary>Effects draw in this order, lowest first, after the background.</summary>
    public static int DrawOrder(EffectKind kind) => kind switch
    {
        EffectKind.Water => 0,
        EffectKind.Rain => 1,
        EffectKind.Snow => 2,
        EffectKind.Glass => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Sets where one effect shows. Call on the render thread; its mask is rebuilt on the next update or render.</summary>
    public void SetRegions(EffectKind kind, RegionSet regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var slot = Slot(kind);
        if (ReferenceEquals(regions, slot.Regions))
        {
            return;
        }
        if (!regions.Equals(slot.Regions))
        {
            slot.IsStale = true;
        }
        slot.Regions = regions;
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        EnsureMasks(targetPixels);
        foreach (var effect in _effects)
        {
            effect.Update(time, targetPixels);
        }
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels)
    {
        EnsureMasks(targetPixels);
        _background.Draw(target, targetPixels, _image, _destination);
        ID3D11ShaderResourceView? frame = null;
        foreach (var kind in s_drawSequence)
        {
            foreach (var effect in _effects)
            {
                if (effect.Kind != kind)
                {
                    continue;
                }
                if (effect.NeedsFrameCopy)
                {
                    frame ??= _frameCopy.Capture(target, targetPixels);
                }
                var mask = Slot(kind).Mask ?? throw new InvalidOperationException($"No mask was built for {kind}.");
                effect.Render(target, targetPixels, new EffectInputs(mask.View, _image, _destination, effect.NeedsFrameCopy ? frame : null));
            }
        }
        if (frame is null)
        {
            _frameCopy.Release();
        }
    }

    private EffectSlot Slot(EffectKind kind)
    {
        if (!_slots.TryGetValue(kind, out var slot))
        {
            slot = new EffectSlot();
            _slots.Add(kind, slot);
        }
        return slot;
    }

    private void EnsureMasks(Size targetPixels)
    {
        foreach (var effect in _effects)
        {
            var slot = Slot(effect.Kind);
            if (slot.Mask is null || slot.IsStale || targetPixels != slot.MaskTarget)
            {
                slot.Mask ??= new RegionMaskTexture(_gpu);
                slot.Mask.Update(RegionMask.Build(slot.Regions, ImageBounds, targetPixels));
                slot.MaskTarget = targetPixels;
                slot.IsStale = false;
                foreach (var sibling in _effects)
                {
                    if (sibling.Kind == effect.Kind)
                    {
                        sibling.SetGeometry(slot.Regions, ImageBounds, targetPixels);
                        _placed.Add(sibling);
                    }
                }
            }
            else if (_placed.Add(effect))
            {
                effect.SetGeometry(slot.Regions, ImageBounds, targetPixels);
            }
        }
    }

    public void Dispose()
    {
        foreach (var effect in _effects)
        {
            effect.Dispose();
        }
        _effects.Clear();
        _placed.Clear();
        foreach (var slot in _slots.Values)
        {
            slot.Mask?.Dispose();
        }
        _slots.Clear();
        _frameCopy.Dispose();
        _image.Dispose();
    }

    private sealed class EffectSlot
    {
        public RegionSet Regions { get; set; } = RegionSet.Empty;

        public RegionMaskTexture? Mask { get; set; }

        public Size MaskTarget { get; set; } = Size.Empty;

        public bool IsStale { get; set; } = true;
    }
}
