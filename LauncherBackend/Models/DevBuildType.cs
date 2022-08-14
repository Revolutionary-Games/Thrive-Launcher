using System.Text.Json.Serialization;
using SharedBase.Converters;

namespace LauncherBackend.Models;

[JsonConverter(typeof(ActualEnumStringConverter))]
public enum DevBuildType
{
    [JsonPropertyName("botd")]
    BuildOfTheDay,

    [JsonPropertyName("latest")]
    Latest,

    [JsonPropertyName("manual")]
    ManuallySelected,
}
