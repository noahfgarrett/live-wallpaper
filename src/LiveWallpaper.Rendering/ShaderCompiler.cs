using System.Reflection;
using Vortice.D3DCompiler;

namespace LiveWallpaper.Rendering;

public static class ShaderCompiler
{
    private const string ResourcePrefix = "LiveWallpaper.Rendering.Shaders.";

    public static ReadOnlyMemory<byte> Compile(string resourceName, string entryPoint, string profile)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + resourceName)
            ?? throw new InvalidOperationException($"Shader resource '{resourceName}' is not embedded.");
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        return Compiler.Compile(source, entryPoint, resourceName, profile, ShaderFlags.OptimizationLevel3);
    }
}
