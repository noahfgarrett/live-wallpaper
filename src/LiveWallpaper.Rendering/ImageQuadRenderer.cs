using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using Size = System.Drawing.Size;

namespace LiveWallpaper.Rendering;

public sealed class ImageQuadRenderer : IDisposable
{
    public const string ShaderResource = "ImageQuad.hlsl";

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _sampler;

    public ImageQuadRenderer(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _constants = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<QuadConstants>(), BindFlags.ConstantBuffer);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    public void Draw(ID3D11RenderTargetView target, Size targetSize, ID3D11ShaderResourceView image, RectangleF destination)
    {
        var context = _gpu.Context;
        var constants = new QuadConstants
        {
            DestRect = new Vector4(destination.X, destination.Y, destination.Width, destination.Height),
            TargetSize = new Vector2(targetSize.Width, targetSize.Height),
        };
        context.UpdateSubresource(in constants, _constants);

        context.OMSetRenderTargets(target);
        context.RSSetViewport(0, 0, targetSize.Width, targetSize.Height);
        context.ClearRenderTargetView(target, new Color4(0, 0, 0, 1));
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _constants);
        context.PSSetShader(_pixelShader);
        context.PSSetShaderResource(0, image);
        context.PSSetSampler(0, _sampler);
        context.Draw(4, 0);
    }

    public void Dispose()
    {
        _sampler.Dispose();
        _constants.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QuadConstants
    {
        public Vector4 DestRect;
        public Vector2 TargetSize;
        public Vector2 Padding;
    }
}
