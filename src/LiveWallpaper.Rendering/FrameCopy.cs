using System.Drawing;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Box = Vortice.Mathematics.Box;

namespace LiveWallpaper.Rendering;

/// <summary>A copy of the frame so far with a full mip chain, for effects that sample what is behind them.</summary>
public sealed class FrameCopy : IDisposable
{
    private readonly GpuDevice _gpu;
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _view;
    private Size _size;

    public FrameCopy(GpuDevice gpu)
    {
        _gpu = gpu;
    }

    /// <summary>Copies <paramref name="target"/>, a B8G8R8A8 target of <paramref name="size"/> pixels, and regenerates the mips. The view stays owned by this copy.</summary>
    public ID3D11ShaderResourceView Capture(ID3D11RenderTargetView target, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The frame must have a positive size.");
        }
        var (texture, view) = Ensure(size);
        using var source = target.Resource;
        _gpu.Context.CopySubresourceRegion(texture, 0, 0, 0, 0, source, 0, new Box(0, 0, 0, size.Width, size.Height, 1));
        _gpu.Context.GenerateMips(view);
        return view;
    }

    /// <summary>Frees the texture; the next capture creates it again.</summary>
    public void Release()
    {
        _view?.Dispose();
        _view = null;
        _texture?.Dispose();
        _texture = null;
        _size = Size.Empty;
    }

    public void Dispose() => Release();

    private (ID3D11Texture2D Texture, ID3D11ShaderResourceView View) Ensure(Size size)
    {
        if (_texture is not null && _view is not null && size == _size)
        {
            return (_texture, _view);
        }
        Release();
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)size.Width, (uint)size.Height, 1, 0,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
        var texture = _gpu.Device.CreateTexture2D(desc);
        try
        {
            _view = _gpu.Device.CreateShaderResourceView(texture);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
        _texture = texture;
        _size = size;
        return (texture, _view);
    }
}
