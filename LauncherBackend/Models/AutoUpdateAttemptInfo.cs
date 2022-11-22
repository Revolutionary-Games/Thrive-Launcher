namespace LauncherBackend.Models;

using System.Text.Json.Serialization;

public class AutoUpdateAttemptInfo
{
    public AutoUpdateAttemptInfo(string previousLauncherVersion)
    {
        PreviousLauncherVersion = previousLauncherVersion;
    }

    public string PreviousLauncherVersion { get; set; }

    [JsonInclude]
    public HashSet<string> UpdateFiles { get; private set; } = new();
}
