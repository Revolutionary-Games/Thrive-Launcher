namespace LauncherBackend.Models;

using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using SharedBase.Converters;

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
