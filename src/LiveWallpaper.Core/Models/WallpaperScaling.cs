using System.Text.Json.Serialization;

namespace LiveWallpaper.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WallpaperScaling>))]
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
