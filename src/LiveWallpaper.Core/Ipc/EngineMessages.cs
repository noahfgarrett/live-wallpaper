using System.Text.Json.Serialization;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ApplyWallpaper), "apply")]
[JsonDerivedType(typeof(UpdateSnow), "snow")]
[JsonDerivedType(typeof(UpdateRain), "rain")]
[JsonDerivedType(typeof(UpdateGlass), "glass")]
[JsonDerivedType(typeof(UpdateWater), "water")]
[JsonDerivedType(typeof(UpdateRegions), "regions")]
[JsonDerivedType(typeof(SetPaused), "pause")]
[JsonDerivedType(typeof(SetPreferences), "prefs")]
[JsonDerivedType(typeof(GetStatus), "status")]
[JsonDerivedType(typeof(Reload), "reload")]
[JsonDerivedType(typeof(Shutdown), "shutdown")]
public abstract record EngineRequest;

public sealed record ApplyWallpaper(Guid LibraryItemId, string MonitorSelector, WallpaperScaling Scaling) : EngineRequest;

public sealed record UpdateSnow(Guid LibraryItemId, SnowSettings Snow) : EngineRequest;

public sealed record UpdateRain(Guid LibraryItemId, RainSettings Rain) : EngineRequest;

public sealed record UpdateGlass(Guid LibraryItemId, GlassSettings Glass) : EngineRequest;

public sealed record UpdateWater(Guid LibraryItemId, WaterSettings Water) : EngineRequest;

public sealed record UpdateRegions(Guid LibraryItemId, EffectKind Effect, RegionSet Regions) : EngineRequest;

public sealed record SetPaused(bool Paused) : EngineRequest;

public sealed record SetPreferences(FrameRateMode FrameRate, WallpaperScaling DefaultScaling, bool KeepRunningWhenAppCloses) : EngineRequest;

public sealed record GetStatus : EngineRequest;

public sealed record Reload : EngineRequest;

public sealed record Shutdown : EngineRequest;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Ok), "ok")]
[JsonDerivedType(typeof(StatusReport), "status")]
[JsonDerivedType(typeof(Failed), "error")]
public abstract record EngineResponse;

public sealed record Ok : EngineResponse;

public sealed record Failed(string Message) : EngineResponse;

public sealed record StatusReport(EngineStatus Status) : EngineResponse;

public sealed record MonitorStatus(string MonitorId, string DeviceName, int Width, int Height, bool IsPrimary, Guid? LibraryItemId, WallpaperScaling? Scaling, bool IsShown);

public sealed record EngineStatus(
    bool IsPaused,
    double Fps,
    long PrivateBytes,
    double CpuPercent,
    int ActiveParticles,
    IReadOnlyList<MonitorStatus> Monitors,
    AppSettings Settings,
    string? Problem,
    int ProtocolVersion = 0);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(EngineRequest))]
[JsonSerializable(typeof(EngineResponse))]
public sealed partial class IpcJsonContext : JsonSerializerContext;
