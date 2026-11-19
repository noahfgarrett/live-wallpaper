using System.Text.Json.Serialization;

namespace LiveWallpaper.Core.Models;

[JsonConverter(typeof(EffectKindJsonConverter))]
public enum EffectKind
{
    [JsonStringEnumMemberName("snow")]
    Snow,

    [JsonStringEnumMemberName("rain")]
    Rain,

    [JsonStringEnumMemberName("glass")]
    Glass,

    [JsonStringEnumMemberName("water")]
    Water,
}

public sealed class EffectKindJsonConverter() : JsonStringEnumConverter<EffectKind>(namingPolicy: null, allowIntegerValues: false);
