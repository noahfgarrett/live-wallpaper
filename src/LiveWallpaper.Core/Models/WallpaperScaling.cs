using System.Text.Json.Serialization;

namespace LiveWallpaper.Core.Models;

[JsonConverter(typeof(WallpaperScalingJsonConverter))]
public enum WallpaperScaling
{
    [JsonStringEnumMemberName("fill")]
    Fill,

    [JsonStringEnumMemberName("fit")]
    Fit,

    [JsonStringEnumMemberName("stretch")]
    Stretch,

    [JsonStringEnumMemberName("center")]
    Center,
}

public sealed class WallpaperScalingJsonConverter() : JsonStringEnumConverter<WallpaperScaling>(namingPolicy: null, allowIntegerValues: false);
