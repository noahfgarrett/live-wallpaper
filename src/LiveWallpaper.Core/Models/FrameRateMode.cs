using System.Text.Json.Serialization;

namespace LiveWallpaper.Core.Models;

[JsonConverter(typeof(FrameRateModeJsonConverter))]
public enum FrameRateMode
{
    [JsonStringEnumMemberName("fps30")]
    Fps30,

    [JsonStringEnumMemberName("fps60")]
    Fps60,

    [JsonStringEnumMemberName("adaptive")]
    Adaptive,
}

public sealed class FrameRateModeJsonConverter() : JsonStringEnumConverter<FrameRateMode>(namingPolicy: null, allowIntegerValues: false);
