using System.Text.Json.Serialization;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
