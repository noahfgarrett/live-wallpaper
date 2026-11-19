using System.Text.Json.Serialization;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LibraryDocument))]
[JsonSerializable(typeof(DesktopSnapshot))]
[JsonSerializable(typeof(DesktopDump))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
