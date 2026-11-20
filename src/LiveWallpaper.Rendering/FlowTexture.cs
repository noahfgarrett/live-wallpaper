using System.Drawing;
using LiveWallpaper.Core.Water;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Rendering;

/// <summary>The water flow field as a two-channel float texture, in target pixels per second. It starts as one still pixel.</summary>
public sealed class FlowTexture : IDisposable
{
    private const uint BytesPerPixel = 2 * sizeof(float);

    private readonly GpuDevice _gpu;
    private ID3D11Texture2D _texture;
    private ID3D11ShaderResourceView _view;

    public FlowTexture(GpuDevice gpu)
    {
        _gpu = gpu;
        PixelSize = new Size(1, 1);
        (_texture, _view) = Create(PixelSize);
        _gpu.Context.UpdateSubresource(new float[2], _texture, 0, BytesPerPixel);
    }

    public ID3D11ShaderResourceView View => _view;

    public Size PixelSize { get; private set; }

    /// <summary>Uploads the field, recreating the texture only when its size changes.</summary>
    public void Update(FlowImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0 || image.Pixels.Length != image.Width * image.Height * 2)
        {
            throw new ArgumentException("The flow image must hold two floats per pixel.", nameof(image));
        }
        var size = new Size(image.Width, image.Height);
        if (size != PixelSize)
        {
            var (texture, view) = Create(size);
            _view.Dispose();
            _texture.Dispose();
            _texture = texture;
            _view = view;
            PixelSize = size;
        }
        _gpu.Context.UpdateSubresource(image.Pixels, _texture, 0, (uint)image.Width * BytesPerPixel);
    }

    public void Dispose()
    {
        _view.Dispose();
        _texture.Dispose();
    }

    private (ID3D11Texture2D Texture, ID3D11ShaderResourceView View) Create(Size size)
    {
        var desc = new Texture2DDescription(Format.R32G32_Float, (uint)size.Width, (uint)size.Height, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        var texture = _gpu.Device.CreateTexture2D(desc);
        try
        {
            return (texture, _gpu.Device.CreateShaderResourceView(texture));
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}
