using System.Drawing;
using LiveWallpaper.Core.Regions;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace LiveWallpaper.Rendering;

/// <summary>Draws the region editor's outlines and handles over the preview.</summary>
public sealed class RegionOverlayRenderer : IDisposable
{
    public const string ShaderResource = "RegionOverlay.hlsl";

    /// <summary>Must match MAX_SHAPES in the shader. Enough for every edge of the largest region set plus one region's handles and the ghost.</summary>
    public const int MaxShapes = 1100;

    private const int HeaderFloats = 4;
    private const int FloatsPerShape = 8;

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly float[] _data = new float[HeaderFloats + (MaxShapes * FloatsPerShape)];

    public RegionOverlayRenderer(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertexShader = gpu.Device.CreateVertexShader(ShaderCompiler.Compile(ShaderResource, "VSMain", "vs_5_0").Span);
        _pixelShader = gpu.Device.CreatePixelShader(ShaderCompiler.Compile(ShaderResource, "PSMain", "ps_5_0").Span);
        _constants = gpu.Device.CreateBuffer((uint)(_data.Length * sizeof(float)), BindFlags.ConstantBuffer);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _noCulling = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    /// <param name="target">The render target.</param>
    /// <param name="targetPixels">Its size in pixels.</param>
    /// <param name="imageBounds">Where the whole source image lands, in pixels; shapes are in image coordinates.</param>
    /// <param name="shapes">What to draw. Shapes past <see cref="MaxShapes"/> are dropped.</param>
    /// <param name="scale">Device pixels per device-independent pixel.</param>
    public void Draw(ID3D11RenderTargetView target, Size targetPixels, RectangleF imageBounds, IReadOnlyList<OverlayShape> shapes, float scale)
    {
        var count = Math.Min(shapes.Count, MaxShapes);
        if (count == 0)
        {
            return;
        }
        _data[0] = targetPixels.Width;
        _data[1] = targetPixels.Height;
        _data[2] = scale;
        _data[3] = count;
        for (var i = 0; i < count; i++)
        {
            var shape = shapes[i];
            var o = HeaderFloats + (i * FloatsPerShape);
            _data[o] = (float)(imageBounds.X + (shape.A.X * imageBounds.Width));
            _data[o + 1] = (float)(imageBounds.Y + (shape.A.Y * imageBounds.Height));
            _data[o + 2] = (float)(imageBounds.X + (shape.B.X * imageBounds.Width));
            _data[o + 3] = (float)(imageBounds.Y + (shape.B.Y * imageBounds.Height));
            _data[o + 4] = (float)shape.Kind;
        }

        var context = _gpu.Context;
        context.UpdateSubresource(_data, _constants);
        context.OMSetRenderTargets(target);
        context.OMSetBlendState(_blend);
        context.RSSetState(_noCulling);
        context.RSSetViewport(0, 0, targetPixels.Width, targetPixels.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _constants);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _constants);
        context.DrawInstanced(4, (uint)count, 0, 0);
    }

    public void Dispose()
    {
        _noCulling.Dispose();
        _blend.Dispose();
        _constants.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }
}
