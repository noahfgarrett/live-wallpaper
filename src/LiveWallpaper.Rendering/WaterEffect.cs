using System.Drawing;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Water;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

/// <summary>Streams the photo's own pixels along each water region's arrow, with foam and a mist plume at the base.</summary>
public sealed class WaterEffect : IEffect
{
    public const string ShaderResource = "Water.hlsl";

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly FlowTexture _flow;
    private readonly WaterPhaseClock _clock = new();
    private WaterSettings _settings = new();
    private WaterFrameConstants _constants;
    private double _phase;
    private bool _hasRegions;

    public WaterEffect(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _frameBuffer = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<WaterFrameConstants>(), BindFlags.ConstantBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _flow = new FlowTexture(gpu);
    }

    public EffectKind Kind => EffectKind.Water;

    public int ActiveParticles => 0;

    public bool IsAnimated => Volatile.Read(ref _settings).Enabled && Volatile.Read(ref _hasRegions);

    public bool NeedsFrameCopy => false;

    public void Apply(WaterSettings settings) => Volatile.Write(ref _settings, settings);

    public void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var hasRegions = regions.Regions.Count > 0;
        if (hasRegions)
        {
            _flow.Update(FlowField.Build(regions, imageBounds, targetPixels, WaterModel.MaxMistReachFraction * imageBounds.Height));
        }
        Volatile.Write(ref _hasRegions, hasRegions);
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        _phase = _clock.Step(time.DeltaSeconds, Volatile.Read(ref _settings).StretchPct);
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs)
    {
        var settings = Volatile.Read(ref _settings);
        if (!settings.Enabled || !Volatile.Read(ref _hasRegions))
        {
            return;
        }
        WaterFrameBuilder.Build(_phase, settings, inputs.ImageDestination, targetPixels, ref _constants);
        var context = _gpu.Context;
        context.UpdateSubresource(in _constants, _frameBuffer);
        context.OMSetRenderTargets(target);
        context.OMSetBlendState(_blend);
        context.RSSetState(_noCulling);
        context.RSSetViewport(0, 0, targetPixels.Width, targetPixels.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _frameBuffer);
        context.PSSetShaderResource(0, inputs.Image);
        context.PSSetShaderResource(1, inputs.Mask);
        context.PSSetShaderResource(2, _flow.View);
        context.PSSetSampler(0, _sampler);
        context.Draw(3, 0);
    }

    public void Dispose()
    {
        _flow.Dispose();
        _noCulling.Dispose();
        _blend.Dispose();
        _sampler.Dispose();
        _frameBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }
}
