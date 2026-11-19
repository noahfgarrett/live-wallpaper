using System.Drawing;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Rendering;

public sealed class WallpaperSurface : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly uint _bufferCount;
    private ID3D11RenderTargetView _backBuffer;

    public WallpaperSurface(GpuDevice gpu, nint hwnd, Size pixelSize, bool useFlipModel)
    {
        _gpu = gpu;
        _bufferCount = useFlipModel ? 2u : 1u;
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, pixelSize.Width),
            Height = (uint)Math.Max(1, pixelSize.Height),
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = _bufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = useFlipModel ? SwapEffect.FlipDiscard : SwapEffect.Discard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };
        using var factory = gpu.GetFactory();
        _swapChain = factory.CreateSwapChainForHwnd(gpu.Device, hwnd, desc);
        _backBuffer = CreateBackBuffer();
        PixelSize = pixelSize;
        UsesFlipModel = useFlipModel;
    }

    public ID3D11RenderTargetView BackBuffer => _backBuffer;

    public Size PixelSize { get; private set; }

    public bool UsesFlipModel { get; }

    public void Resize(Size pixelSize)
    {
        _gpu.Context.OMSetRenderTargets(0u, Array.Empty<ID3D11RenderTargetView>());
        _backBuffer.Dispose();
        _swapChain.ResizeBuffers(_bufferCount, (uint)Math.Max(1, pixelSize.Width), (uint)Math.Max(1, pixelSize.Height), Format.Unknown, SwapChainFlags.None).CheckError();
        _backBuffer = CreateBackBuffer();
        PixelSize = pixelSize;
    }

    public void Present() => _swapChain.Present(1, PresentFlags.None).CheckError();

    public void Dispose()
    {
        _gpu.Context.OMSetRenderTargets(0u, Array.Empty<ID3D11RenderTargetView>());
        _backBuffer.Dispose();
        _swapChain.Dispose();
    }

    private ID3D11RenderTargetView CreateBackBuffer()
    {
        using var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        return _gpu.Device.CreateRenderTargetView(buffer);
    }
}
