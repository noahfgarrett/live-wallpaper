using LiveWallpaper.Rendering;

namespace LiveWallpaper.Windows.Tests;

public sealed class ShaderCompilerTests
{
    [Theory]
    [InlineData("VSMain", "vs_5_0")]
    [InlineData("PSMain", "ps_5_0")]
    public void Compile_ImageQuadShaders_ReturnsBytecode(string entryPoint, string profile)
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(ImageQuadRenderer.ShaderResource, entryPoint, profile).IsEmpty);
    }

    [Theory]
    [InlineData("VSMain", "vs_5_0")]
    [InlineData("PSMain", "ps_5_0")]
    public void Rain_Compiles(string entryPoint, string profile)
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(RainEffect.ShaderResource, entryPoint, profile).IsEmpty);
    }

    [Theory]
    [InlineData("VSMain", "vs_5_0")]
    [InlineData("PSMain", "ps_5_0")]
    public void Water_Compiles(string entryPoint, string profile)
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(WaterEffect.ShaderResource, entryPoint, profile).IsEmpty);
    }

    [Theory]
    [InlineData("VSMain", "vs_5_0")]
    [InlineData("PSMain", "ps_5_0")]
    public void Glass_Compiles(string entryPoint, string profile)
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(GlassEffect.ShaderResource, entryPoint, profile).IsEmpty);
    }

    [Theory]
    [InlineData("VSMain", "vs_5_0")]
    [InlineData("PSMain", "ps_5_0")]
    public void PreviewBlit_Compiles(string entryPoint, string profile)
    {
        WindowsOnly.Require();

        Assert.False(ShaderCompiler.Compile(PreviewPresenter.ShaderResource, entryPoint, profile).IsEmpty);
    }

    [Fact]
    public void Compile_MissingResource_Throws()
    {
        WindowsOnly.Require();

        Assert.Throws<InvalidOperationException>(() => ShaderCompiler.Compile("Nope.hlsl", "VSMain", "vs_5_0"));
    }
}
