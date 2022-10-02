namespace LauncherBackend.Models;

using System.Text.Json.Serialization;
using SharedBase.Models;

public class LauncherSettings
{
    // General launcher options
    [JsonPropertyName("fetchNewsFromWeb")]
    public bool ShowWebContent { get; set; } = true;

    [JsonPropertyName("hideLauncherOnPlay")]
    public bool HideLauncherOnPlay { get; set; } = true;

    [JsonPropertyName("hide32bit")]
    public bool Hide32Bit { get; set; } = true;

    [JsonPropertyName("closeLauncherAfterGameExit")]
    public bool CloseLauncherAfterGameExit { get; set; }

    [JsonPropertyName("closeLauncherOnGameStart")]
    public bool CloseLauncherOnGameStart { get; set; }

    [JsonPropertyName("storeVersionShowExternalVersions")]
    public bool StoreVersionShowExternalVersions { get; set; }

    [JsonPropertyName("autoStartStoreVersion")]
    public bool AutoStartStoreVersion { get; set; }

    [JsonPropertyName("beginningKeptGameOutput")]
    public int BeginningKeptGameOutput { get; set; } = 100;

    [JsonPropertyName("lastKeptGameOutput")]
    public int LastKeptGameOutput { get; set; } = 900;

    [JsonPropertyName("launcherLanguage")]
    public string? SelectedLauncherLanguage { get; set; }

    // Path options (null if default is used)
    [JsonPropertyName("installPath")]
    public string? ThriveInstallationPath { get; set; }

    [JsonPropertyName("cacheFolderPath")]
    public string? DehydratedCacheFolder { get; set; }

    [JsonPropertyName("temporaryFolder")]
    public string? TemporaryDownloadsFolder { get; set; }

    // DevCenter options
    [JsonPropertyName("devCenterKey")]
    public string? DevCenterKey { get; set; }

    [JsonPropertyName("selectedDevBuildType")]
    public DevBuildType SelectedDevBuildType { get; set; } = DevBuildType.BuildOfTheDay;

    [JsonPropertyName("manuallySelectedBuildHash")]
    public string? ManuallySelectedBuildHash { get; set; }

    // Thrive start options
    [JsonPropertyName("forceGLES2Mode")]
    public bool ForceGles2Mode { get; set; }

    [JsonPropertyName("disableThriveVideos")]
    public bool DisableThriveVideos { get; set; }

    public bool ShouldShowVersionWithPlatform(PackagePlatform versionPlatform)
    {
        switch (versionPlatform)
        {
            case PackagePlatform.Linux:
                return OperatingSystem.IsLinux();
            case PackagePlatform.Windows:
                return OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;
            case PackagePlatform.Windows32:
                return OperatingSystem.IsWindows() && (!Environment.Is64BitOperatingSystem || !Hide32Bit);
            case PackagePlatform.Mac:
                return OperatingSystem.IsMacOS();
            default:
                throw new ArgumentOutOfRangeException(nameof(versionPlatform), versionPlatform, null);
        }
    }
}
