using System.Drawing;
using System.Runtime.InteropServices;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;
using LiveWallpaper.Core.Snow;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Rendering;

public sealed unsafe class SnowEffect : IEffect
{
    public const string ShaderResource = "Snow.hlsl";
    private const int LayerTableFloats = 40;

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11Buffer _layerBuffer;
    private readonly ID3D11ShaderResourceView _atlas;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly FallClock _fallClock = new();
    private readonly WindModel _wind;
    private SnowSettings _settings = new();
    private SnowFrameConstants _constants;
    private int _count;

    public SnowEffect(GpuDevice gpu, uint seed)
    {
        _gpu = gpu;
        _wind = new WindModel(seed);
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _frameBuffer = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<SnowFrameConstants>(), BindFlags.ConstantBuffer);
        _layerBuffer = gpu.Device.CreateBuffer(LayerTableFloats * sizeof(float), BindFlags.ConstantBuffer);
        gpu.Context.UpdateSubresource(BuildLayerTable(), _layerBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);

        var pixels = FlakeAtlas.GenerateBgra();
        const uint width = FlakeAtlas.CellPx * FlakeAtlas.Cells;
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, width, FlakeAtlas.CellPx, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        fixed (byte* p = pixels)
        {
            using var texture = gpu.Device.CreateTexture2D(desc, new SubresourceData(p, width * 4, 0));
            _atlas = gpu.Device.CreateShaderResourceView(texture);
        }
    }

    public EffectKind Kind => EffectKind.Snow;

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

    public void Apply(SnowSettings settings) => Volatile.Write(ref _settings, settings);

    public void SetGeometry(RegionSet regions, RectangleF imageBounds, Size targetPixels)
    {
    }

    public void Update(in FrameTime time, Size targetPixels)
    {
        var s = Volatile.Read(ref _settings);
        var fallTime = _fallClock.Step(time.DeltaSeconds, s.SpeedPct);
        var (windSpeed, windOffset) = _wind.Step(time.DeltaSeconds, s.WindPct, targetPixels.Height);
        _count = s.Enabled ? SnowModel.FlakeCount(s.DensityPct, targetPixels.Width * (double)targetPixels.Height / 1e6) : 0;
        SnowFrameBuilder.Build(fallTime, windOffset, windSpeed, time.TotalSeconds, s.SpeedPct, s.SizePct, s.MotionBlur, targetPixels, ref _constants);
        _constants.FlakeCount = _count;
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
        context.PSSetShaderResource(0, _atlas);
        context.PSSetShaderResource(1, inputs.Mask);
        context.PSSetSampler(0, _sampler);
        context.DrawInstanced(4, (uint)_count, 0, 0);
    }

    public void Dispose()
    {
        _noCulling.Dispose();
        _blend.Dispose();
        _sampler.Dispose();
        _atlas.Dispose();
        _layerBuffer.Dispose();
        _frameBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    private static float[] BuildLayerTable()
    {
        var layers = SnowModel.Layers;
        var table = new float[LayerTableFloats];
        var cumulative = 0f;
        for (var i = 0; i < 3; i++)
        {
            var l = layers[i];
            Write(table, i, l.SizeMin, l.SizeMax, l.FallMin, l.FallMax);
            Write(table, 3 + i, l.OpacityMin, l.OpacityMax, l.Softness, l.WindResponse);
            Write(table, 6 + i, l.SwayMin, l.SwayMax, l.SwayHzMin, l.SwayHzMax);
            cumulative += l.Share;
            table[36 + i] = cumulative;
        }
        table[38] = 1.01f;
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
