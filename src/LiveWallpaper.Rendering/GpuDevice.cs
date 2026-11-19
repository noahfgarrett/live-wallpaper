using LiveWallpaper.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveWallpaper.Rendering;

public sealed unsafe class GpuDevice : IDisposable
{
    private static readonly FeatureLevel[] s_featureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    private GpuDevice(ID3D11Device device, ID3D11DeviceContext context)
    {
        Device = device;
        Context = context;
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public static GpuDevice Create(bool useWarp = false)
    {
        D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            useWarp ? DriverType.Warp : DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            s_featureLevels,
            out var device,
            out var context).CheckError();
        return new GpuDevice(device!, context!);
    }

    public ID3D11ShaderResourceView CreateImageTexture(DecodedImage image)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)image.Width, (uint)image.Height, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        fixed (byte* pixels = image.Pixels)
        {
            using var texture = Device.CreateTexture2D(desc, new SubresourceData(pixels, (uint)image.Stride, 0));
            return Device.CreateShaderResourceView(texture);
        }
    }

    public IDXGIFactory2 GetFactory()
    {
        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        return adapter.GetParent<IDXGIFactory2>();
    }

    public void Dispose()
    {
        Context.ClearState();
        Context.Flush();
        Context.Dispose();
        Device.Dispose();
    }
}
