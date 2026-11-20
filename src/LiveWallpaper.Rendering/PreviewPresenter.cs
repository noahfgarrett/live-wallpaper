using System.Numerics;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Size = System.Drawing.Size;

namespace LiveWallpaper.Rendering;

/// <summary>
/// Holds the editor preview's desktop-sized offscreen target and shows the camera's view of it, sharp at every zoom:
/// Catmull-Rom from the mip that leaves at most a 2x reduction.
/// </summary>
public sealed class PreviewPresenter : IDisposable
{
    public const string ShaderResource = "PreviewBlit.hlsl";

    private static readonly Color4 s_background = new(0.05f, 0.08f, 0.13f, 1f);

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constantBuffer;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _opaque;
    private readonly ID3D11RasterizerState _noCulling;
    private ID3D11Texture2D? _texture;
    private ID3D11RenderTargetView? _targetView;
    private ID3D11ShaderResourceView? _shaderView;
    private Size _size;
    private int _mipLevels;
    private BlitConstants _constants;

    public PreviewPresenter(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _constantBuffer = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<BlitConstants>(), BindFlags.ConstantBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _opaque = gpu.Device.CreateBlendState(BlendDescription.Opaque);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    /// <summary>The offscreen target of <paramref name="render"/> pixels for the scene to draw into, recreated only when the size changes.</summary>
    public ID3D11RenderTargetView Begin(Size render)
    {
        if (render.Width <= 0 || render.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(render), render, "The render size must be positive.");
        }
        if (_targetView is not null && render == _size)
        {
            return _targetView;
        }
        ReleaseTarget();
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)render.Width, (uint)render.Height, 0, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
        var texture = _gpu.Device.CreateTexture2D(desc);
        try
        {
            _shaderView = _gpu.Device.CreateShaderResourceView(texture);
            _targetView = _gpu.Device.CreateRenderTargetView(texture);
        }
        catch
        {
            ReleaseTarget();
            texture.Dispose();
            throw;
        }
        _texture = texture;
        _size = render;
        _mipLevels = (int)texture.Description.MipLevels;
        return _targetView;
    }

    /// <summary>Clears <paramref name="target"/> to the editor background and draws the camera's view of the offscreen target over it.</summary>
    public void Present(ID3D11RenderTargetView target, Size view, PreviewCamera camera)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(camera);
        if (_shaderView is null)
        {
            throw new InvalidOperationException("Begin must be called before Present.");
        }
        if (camera.Render != _size)
        {
            throw new ArgumentException($"The camera's render size {camera.Render} does not match the target's {_size}.", nameof(camera));
        }
        if (!double.IsFinite(camera.Scale) || camera.Scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), camera.Scale, "The camera's scale must be positive.");
        }
        if (view.Width <= 0 || view.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(view), view, "The view must have a positive size.");
        }

        _constants.RenderSize = new Vector2(_size.Width, _size.Height);
        _constants.ViewToRenderScale = (float)(1.0 / camera.Scale);
        _constants.Mip = MipFor(camera.Scale);
        _constants.Offset = new Vector2(camera.Offset.X, camera.Offset.Y);
        _constants.ViewSize = new Vector2(view.Width, view.Height);

        var context = _gpu.Context;
        context.OMSetRenderTargets(target);
        context.GenerateMips(_shaderView);
        context.UpdateSubresource(in _constants, _constantBuffer);
        context.ClearRenderTargetView(target, s_background);
        context.OMSetBlendState(_opaque);
        context.RSSetState(_noCulling);
        context.RSSetViewport(0, 0, view.Width, view.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _constantBuffer);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _constantBuffer);
        context.PSSetShaderResource(0, _shaderView);
        context.PSSetSampler(0, _sampler);
        context.Draw(4, 0);
        context.PSUnsetShaderResource(0);
    }

    public void Dispose()
    {
        ReleaseTarget();
        _noCulling.Dispose();
        _opaque.Dispose();
        _sampler.Dispose();
        _constantBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    private float MipFor(double scale)
    {
        var mip = Math.Max(0, (int)Math.Floor(Math.Log2(1.0 / scale)));
        return Math.Min(mip, _mipLevels - 1);
    }

    private void ReleaseTarget()
    {
        _targetView?.Dispose();
        _targetView = null;
        _shaderView?.Dispose();
        _shaderView = null;
        _texture?.Dispose();
        _texture = null;
        _size = Size.Empty;
        _mipLevels = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlitConstants
    {
        public Vector2 RenderSize;
        public float ViewToRenderScale;
        public float Mip;
        public Vector2 Offset;
        public Vector2 ViewSize;
    }
}
