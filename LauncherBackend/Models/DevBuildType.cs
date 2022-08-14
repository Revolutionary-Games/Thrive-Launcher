using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using SharedBase.Converters;

namespace LauncherBackend.Models;

[JsonConverter(typeof(ActualEnumStringConverter))]
public enum DevBuildType
{
    [EnumMember(Value = "botd")]
    BuildOfTheDay,

    [EnumMember(Value = "latest")]
    Latest,

    [EnumMember(Value = "manual")]
    ManuallySelected,
}
