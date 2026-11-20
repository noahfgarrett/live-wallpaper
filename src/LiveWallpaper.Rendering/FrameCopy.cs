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
    private ID3D11RenderTargetView? _sourceTarget;
    private ID3D11Resource? _source;

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
        var source = Source(target);
        _gpu.Context.CopySubresourceRegion(texture, 0, 0, 0, 0, source, 0, new Box(0, 0, 0, size.Width, size.Height, 1));
        _gpu.Context.GenerateMips(view);
        return view;
    }

    /// <summary>Frees the texture; the next capture creates it again.</summary>
    public void Release()
    {
        DropSource();
        _view?.Dispose();
        _view = null;
        _texture?.Dispose();
        _texture = null;
        _size = Size.Empty;
    }

    public void Dispose() => Release();

    /// <summary>
    /// The resource behind <paramref name="target"/>, wrapped once per target so capturing allocates nothing.
    /// The wrapper holds no reference of its own: the target keeps the resource alive, and a swap chain can still
    /// resize its buffers, which fails while any other reference to them is held.
    /// </summary>
    private ID3D11Resource Source(ID3D11RenderTargetView target)
    {
        if (_source is not null && ReferenceEquals(target, _sourceTarget))
        {
            return _source;
        }
        DropSource();
        using var owned = target.Resource;
        _source = new ID3D11Resource(owned.NativePointer);
        _sourceTarget = target;
        return _source;
    }

    private void DropSource()
    {
        if (_source is not null)
        {
            _source.NativePointer = IntPtr.Zero;
            _source.Dispose();
        }
        _source = null;
        _sourceTarget = null;
    }

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
