using System.Drawing;
using LiveWallpaper.Media;
using LiveWallpaper.Rendering;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Windows.Tests;

internal sealed unsafe class OffscreenTarget : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11Texture2D _texture;
    private readonly Texture2DDescription _desc;

    public OffscreenTarget(GpuDevice gpu, Size size)
    {
        _gpu = gpu;
        Size = size;
        _desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)size.Width, (uint)size.Height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        _texture = gpu.Device.CreateTexture2D(_desc);
        View = gpu.Device.CreateRenderTargetView(_texture);
    }

    public Size Size { get; }

    public ID3D11RenderTargetView View { get; }

    public byte[] ReadBack() => ReadMip0(_texture);

    /// <summary>Reads mip 0 of the texture behind <paramref name="view"/>, which must be target-sized B8G8R8A8.</summary>
    public byte[] ReadBack(ID3D11ShaderResourceView view)
    {
        using var resource = view.Resource;
        using var texture = resource.QueryInterface<ID3D11Texture2D>();
        return ReadMip0(texture);
    }

    private byte[] ReadMip0(ID3D11Texture2D source)
    {
        var stagingDesc = _desc with { BindFlags = BindFlags.None, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read };
        using var staging = _gpu.Device.CreateTexture2D(stagingDesc);
        _gpu.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, source, 0);
        var mapped = _gpu.Context.Map(staging, 0, MapMode.Read);
        try
        {
            var result = new byte[Size.Width * Size.Height * 4];
            for (var y = 0; y < Size.Height; y++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.DataPointer + (y * mapped.RowPitch), Size.Width * 4)
                    .CopyTo(result.AsSpan(y * Size.Width * 4));
            }
            return result;
        }
        finally
        {
            _gpu.Context.Unmap(staging, 0);
        }
    }

    public static DecodedImage Solid(byte b, byte g, byte r, int width = 2, int height = 2)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return new DecodedImage(width, height, width * 4, pixels);
    }

    public void Dispose()
    {
        View.Dispose();
        _texture.Dispose();
    }
}
