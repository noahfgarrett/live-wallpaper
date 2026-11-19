using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Glass;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

/// <summary>Rain on a window: beads, sliding drops with trails and fog, drawn per pane over a copy of the frame so far.</summary>
public sealed class GlassEffect : IEffect
{
    public const string ShaderResource = "Glass.hlsl";

    /// <summary>The mask's blur (σ = featherPx / 2) reaches about 3σ, and scaling the smaller mask up adds about σ more.</summary>
    private const double FeatherReachPerFeatherPx = 2;

    /// <summary>The mask's antialiased edge can cover a pixel whose centre is just outside the pane.</summary>
    private const double EdgeReachPx = 1;

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11Buffer _paneBuffer;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _opaque;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly GlassPane[] _panes = new GlassPane[GlassModel.MaxPanes];
    private GlassSettings _settings = new();
    private GlassFrameConstants _constants;
    private double _timeSec;
    private int _paneCount;

    public GlassEffect(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _frameBuffer = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<GlassFrameConstants>(), BindFlags.ConstantBuffer);
        _paneBuffer = gpu.Device.CreateBuffer((uint)(Marshal.SizeOf<GlassPane>() * GlassModel.MaxPanes), BindFlags.ConstantBuffer);
        gpu.Context.UpdateSubresource(_panes, _paneBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _opaque = gpu.Device.CreateBlendState(BlendDescription.Opaque);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public EffectKind Kind => EffectKind.Glass;

    public int ActiveParticles => 0;

    public bool IsAnimated => IsDrawing;

    public bool NeedsFrameCopy => IsDrawing;

    /// <summary>Enabled with at least one usable pane; with none, the frame isn't copied for nothing.</summary>
    private bool IsDrawing => Volatile.Read(ref _settings).Enabled && Volatile.Read(ref _paneCount) > 0;

    public void Apply(GlassSettings settings) => Volatile.Write(ref _settings, settings);

    public void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var count = GlassFrameBuilder.BuildPanes(regions, imageBounds, targetPixels, _panes);
        var reach = regions.Regions.Count == 0
            ? 0
            : (FeatherReachPerFeatherPx * Math.Max(0, regions.FeatherPct) / 100 * imageBounds.Width) + EdgeReachPx;
        for (var i = 0; i < count; i++)
        {
            _panes[i].BoundsPx = Grow(_panes[i].BoundsPx, reach, targetPixels);
        }
        _gpu.Context.UpdateSubresource(_panes, _paneBuffer);
        Volatile.Write(ref _paneCount, count);
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        _timeSec = time.TotalSeconds;
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs)
    {
        var settings = Volatile.Read(ref _settings);
        var paneCount = Volatile.Read(ref _paneCount);
        if (!settings.Enabled || paneCount == 0 || inputs.Frame is not { } frame)
        {
            return;
        }
        GlassFrameBuilder.BuildFrame(_timeSec, settings, targetPixels, paneCount, ref _constants);
        var context = _gpu.Context;
        context.UpdateSubresource(in _constants, _frameBuffer);
        context.OMSetRenderTargets(target);
        context.OMSetBlendState(_opaque);
        context.RSSetState(_noCulling);
        context.RSSetViewport(0, 0, targetPixels.Width, targetPixels.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _frameBuffer);
        context.VSSetConstantBuffer(1, _paneBuffer);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _frameBuffer);
        context.PSSetConstantBuffer(1, _paneBuffer);
        context.PSSetShaderResource(0, frame);
        context.PSSetShaderResource(1, inputs.Mask);
        context.PSSetSampler(0, _sampler);
        context.DrawInstanced(4, (uint)paneCount, 0, 0);
    }

    public void Dispose()
    {
        _noCulling.Dispose();
        _opaque.Dispose();
        _sampler.Dispose();
        _paneBuffer.Dispose();
        _frameBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>Grows a pane's box by the mask's soft edge, so the fade outside the pane is drawn too.</summary>
    private static Vector4 Grow(Vector4 bounds, double reach, Size target)
    {
        var left = Math.Max(0, bounds.X - reach);
        var top = Math.Max(0, bounds.Y - reach);
        var right = Math.Min(target.Width, bounds.X + bounds.Z + reach);
        var bottom = Math.Min(target.Height, bounds.Y + bounds.W + reach);
        return new Vector4((float)left, (float)top, (float)(right - left), (float)(bottom - top));
    }
}
