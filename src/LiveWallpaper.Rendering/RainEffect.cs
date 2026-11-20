using System.Drawing;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Rain;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Snow;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

public sealed class RainEffect : IEffect
{
    public const string ShaderResource = "Rain.hlsl";
    private const int LayerTableFloats = 28;

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11Buffer _layerBuffer;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly FallClock _fallClock = new();
    private readonly WindModel _wind;
    private RainSettings _settings = new();
    private RainFrameConstants _constants;
    private int _count;

    public RainEffect(GpuDevice gpu, uint seed)
    {
        _gpu = gpu;
        _wind = new WindModel(seed, RainModel.WindBaseAtFull);
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _frameBuffer = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<RainFrameConstants>(), BindFlags.ConstantBuffer);
        _layerBuffer = gpu.Device.CreateBuffer(LayerTableFloats * sizeof(float), BindFlags.ConstantBuffer);
        gpu.Context.UpdateSubresource(BuildLayerTable(), _layerBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public EffectKind Kind => EffectKind.Rain;

    public int ActiveParticles => _count;

    public bool IsAnimated
    {
        get
        {
            var s = Volatile.Read(ref _settings);
            return s.Enabled && s.DensityPct > 0;
        }
    }

    public bool NeedsFrameCopy => false;

    public void Apply(RainSettings settings) => Volatile.Write(ref _settings, settings);

    public void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels)
    {
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        var s = Volatile.Read(ref _settings);
        var fallTime = _fallClock.StepScaled(time.DeltaSeconds, RainModel.SpeedMultiplier(s.SpeedPct));
        var (windSpeed, windOffset) = _wind.Step(time.DeltaSeconds, s.WindPct, targetPixels.Height);
        _count = s.Enabled ? RainModel.StreakCount(s.DensityPct, targetPixels.Width * (double)targetPixels.Height / 1e6) : 0;
        RainFrameBuilder.Build(fallTime, windOffset, windSpeed, s, targetPixels, ref _constants);
    }

    public void Render(ID3D11RenderTargetView target, Size targetPixels, in EffectInputs inputs)
    {
        if (_count == 0 || !Volatile.Read(ref _settings).Enabled)
        {
            return;
        }
        var context = _gpu.Context;
        context.UpdateSubresource(in _constants, _frameBuffer);
        context.OMSetRenderTargets(target);
        context.OMSetBlendState(_blend);
        context.RSSetState(_noCulling);
        context.RSSetViewport(0, 0, targetPixels.Width, targetPixels.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _frameBuffer);
        context.VSSetConstantBuffer(1, _layerBuffer);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _frameBuffer);
        context.PSSetShaderResource(1, inputs.Mask);
        context.PSSetSampler(0, _sampler);
        context.DrawInstanced(4, (uint)_count, 0, 0);
    }

    public void Dispose()
    {
        _noCulling.Dispose();
        _blend.Dispose();
        _sampler.Dispose();
        _layerBuffer.Dispose();
        _frameBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    private static float[] BuildLayerTable()
    {
        var layers = RainModel.Layers;
        var table = new float[LayerTableFloats];
        var cumulative = 0f;
        for (var i = 0; i < 3; i++)
        {
            var l = layers[i];
            Write(table, i, l.WidthMin, l.WidthMax, l.FallMin, l.FallMax);
            Write(table, 3 + i, l.OpacityMin, l.OpacityMax, 0, l.WindResponse);
            cumulative += l.Share;
            table[24 + i] = cumulative;
        }
        table[26] = 1.01f;
        return table;
    }

    private static void Write(float[] table, int row, float x, float y, float z, float w)
    {
        table[row * 4] = x;
        table[(row * 4) + 1] = y;
        table[(row * 4) + 2] = z;
        table[(row * 4) + 3] = w;
    }
}
