using System.Drawing;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Rendering;

/// <summary>The region mask as a single-channel texture. With no regions it is one white pixel, so effects show everywhere.</summary>
public sealed unsafe class RegionMaskTexture : IDisposable
{
    private static readonly MaskImage White = new(1, 1, [255]);

    private readonly GpuDevice _gpu;
    private ID3D11ShaderResourceView _view;
    private bool _isWhite = true;

    public RegionMaskTexture(GpuDevice gpu)
    {
        _gpu = gpu;
        _view = Create(White);
        PixelSize = new Size(1, 1);
    }

    public ID3D11ShaderResourceView View => _view;

    public Size PixelSize { get; private set; }

    /// <summary>Replaces the texture. <c>null</c> means no regions.</summary>
    public void Update(MaskImage? mask)
    {
        if (mask is null && _isWhite)
        {
            return;
        }
        var image = mask ?? White;
        var next = Create(image);
        _view.Dispose();
        _view = next;
        _isWhite = mask is null;
        PixelSize = new Size(image.Width, image.Height);
    }

    public void Dispose() => _view.Dispose();

    private ID3D11ShaderResourceView Create(MaskImage image)
    {
        var desc = new Texture2DDescription(Format.R8_UNorm, (uint)image.Width, (uint)image.Height, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        fixed (byte* pixels = image.Pixels)
        {
            using var texture = _gpu.Device.CreateTexture2D(desc, new SubresourceData(pixels, (uint)image.Width, 0));
            return _gpu.Device.CreateShaderResourceView(texture);
        }
    }
}
