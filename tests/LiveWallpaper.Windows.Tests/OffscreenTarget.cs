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

    public byte[] ReadBack() => ReadMip(_texture, 0);

    /// <summary>Reads mip 0 of the texture behind <paramref name="view"/>.</summary>
    public byte[] ReadBack(ID3D11ShaderResourceView view)
    {
        using var resource = view.Resource;
        using var texture = resource.QueryInterface<ID3D11Texture2D>();
        return ReadMip(texture, 0);
    }

    /// <summary>Reads the smallest mip of the texture behind <paramref name="view"/>.</summary>
    public byte[] ReadBackSmallestMip(ID3D11ShaderResourceView view)
    {
        using var resource = view.Resource;
        using var texture = resource.QueryInterface<ID3D11Texture2D>();
        return ReadMip(texture, texture.Description.MipLevels - 1);
    }

    private byte[] ReadMip(ID3D11Texture2D source, uint mip)
    {
        var desc = source.Description;
        var width = (int)Math.Max(1, desc.Width >> (int)mip);
        var height = (int)Math.Max(1, desc.Height >> (int)mip);
        var stagingDesc = _desc with
        {
            Width = (uint)width,
            Height = (uint)height,
            BindFlags = BindFlags.None,
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        using var staging = _gpu.Device.CreateTexture2D(stagingDesc);
        _gpu.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, source, mip);
        var mapped = _gpu.Context.Map(staging, 0, MapMode.Read);
        try
        {
            var result = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.DataPointer + (y * mapped.RowPitch), width * 4)
                    .CopyTo(result.AsSpan(y * width * 4));
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
