namespace LauncherBackend.Models;

using System.Text.Json.Serialization;
using Services;
using SharedBase.Models;

public class LauncherSettings
{
    // General launcher options
    [JsonPropertyName("fetchNewsFromWeb")]
    public bool ShowWebContent { get; set; } = true;

    [JsonPropertyName("hideLauncherOnPlay2")]
    public bool HideLauncherOnPlay { get; set; } = GetDefaultHideOnPlayOption();

    [JsonPropertyName("hide32bit")]
    public bool Hide32Bit { get; set; } = true;

    [JsonPropertyName("closeLauncherAfterGameExit")]
    public bool CloseLauncherAfterGameExit { get; set; }

    [JsonPropertyName("closeLauncherOnGameStart")]
    public bool CloseLauncherOnGameStart { get; set; }

    [JsonPropertyName("storeVersionShowExternalVersions")]
    public bool StoreVersionShowExternalVersions { get; set; }

    [JsonPropertyName("useStoreSeamlessMode")]
    public bool EnableStoreVersionSeamlessMode { get; set; } = true;

    [JsonPropertyName("beginningKeptGameOutput")]
    public int BeginningKeptGameOutput { get; set; } = LauncherConstants.DefaultFirstLinesToKeepOfThriveOutput;

    [JsonPropertyName("lastKeptGameOutput")]
    public int LastKeptGameOutput { get; set; } = LauncherConstants.DefaultLastLinesToKeepOfThriveOutput;

    [JsonPropertyName("launcherLanguage")]
    public string? SelectedLauncherLanguage { get; set; }

    // Path options (null if default is used)
    [JsonPropertyName("installPath")]
    public string? ThriveInstallationPath { get; set; }

    [JsonPropertyName("cacheFolderPath")]
    public string? DehydratedCacheFolder { get; set; }

    [JsonPropertyName("temporaryFolder2")]
    public string? TemporaryDownloadsFolder { get; set; }

    [JsonPropertyName("autoCleanTemporaryFolder")]
    public bool AutoCleanTemporaryFolder { get; set; } = true;

    [JsonPropertyName("allowAutoUpdate")]
    public bool AllowAutoUpdate { get; set; } = true;

    [JsonPropertyName("useAlternateUpdateMethod")]
    public bool UseAlternateUpdateMethod { get; set; }

    [JsonPropertyName("showLatestBetaVersion")]
    public bool ShowLatestBetaVersion { get; set; }

    [JsonPropertyName("showAllBetaVersions")]
    public bool ShowAllBetaVersions { get; set; }

    /// <summary>
    ///   Thrive is restarted if it fails to run (and the played Thrive version supports proper startup detection)
    /// </summary>
    [JsonPropertyName("enableThriveAutoRestart")]
    public bool EnableThriveAutoRestart { get; set; } = true;

    [JsonPropertyName("preferSystemTools")]
    public bool PreferSystemTools { get; set; }

    [JsonPropertyName("verboseLogging")]
    public bool VerboseLogging { get; set; }

    // DevCenter options
    [JsonPropertyName("devCenterKey")]
    public string? DevCenterKey { get; set; }

    [JsonPropertyName("selectedDevBuildType")]
    public DevBuildType? SelectedDevBuildType { get; set; } = DevBuildType.BuildOfTheDay;

    [JsonPropertyName("manuallySelectedBuildHash")]
    public string? ManuallySelectedBuildHash { get; set; }

    // Thrive start options
    [JsonPropertyName("forceOpenGLMode")]
    public bool ForceOpenGlMode { get; set; }

    [JsonPropertyName("disableThriveVideos")]
    public bool DisableThriveVideos { get; set; }

    [JsonPropertyName("disableThriveMods")]
    public bool DisableThriveMods { get; set; }

    [JsonPropertyName("overrideAudioLatency")]
    public bool OverrideAudioLatency { get; set; }

    /// <summary>
    ///   The value to set audio override to, only applies when <see cref="OverrideAudioLatency"/>
    /// </summary>
    [JsonPropertyName("overrideAudioLatencyMilliseconds")]
    public int AudioLatencyMilliseconds { get; set; } = 30;

    public bool ShouldShowVersionWithPlatform(PackagePlatform versionPlatform,
        IEnumerable<PackagePlatform> allPlatformsForSameVersion)
    {
        switch (versionPlatform)
        {
            case PackagePlatform.Linux:
                return OperatingSystem.IsLinux();
            case PackagePlatform.Windows:
                return OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;
            case PackagePlatform.Windows32:
            {
                if (!OperatingSystem.IsWindows())
                    return false;

                // Show 32-bit when we are on a 32-bit platform (or we want to explicitly show)
                if (!Environment.Is64BitOperatingSystem || !Hide32Bit)
                    return true;

                // Otherwise only show the 32-bit version on a 64-bit platform if there is no 64-bit version
                return allPlatformsForSameVersion.All(p => p != PackagePlatform.Windows);
            }

            case PackagePlatform.Mac:
                return OperatingSystem.IsMacOS();
            default:
                throw new ArgumentOutOfRangeException(nameof(versionPlatform), versionPlatform, null);
        }
    }

    public LauncherSettings CloneWithoutSensitiveData()
    {
        // Skip sensitive data like tokens but clone everything else
        return new LauncherSettings
        {
            ShowWebContent = ShowWebContent,
            HideLauncherOnPlay = HideLauncherOnPlay,
            Hide32Bit = Hide32Bit,
            CloseLauncherAfterGameExit = CloseLauncherAfterGameExit,
            CloseLauncherOnGameStart = CloseLauncherOnGameStart,
            StoreVersionShowExternalVersions = StoreVersionShowExternalVersions,
            EnableStoreVersionSeamlessMode = EnableStoreVersionSeamlessMode,
            BeginningKeptGameOutput = BeginningKeptGameOutput,
            LastKeptGameOutput = LastKeptGameOutput,
            SelectedLauncherLanguage = SelectedLauncherLanguage,
            ThriveInstallationPath = ThriveInstallationPath,
            DehydratedCacheFolder = DehydratedCacheFolder,
            TemporaryDownloadsFolder = TemporaryDownloadsFolder,
            AutoCleanTemporaryFolder = AutoCleanTemporaryFolder,
            AllowAutoUpdate = AllowAutoUpdate,
            UseAlternateUpdateMethod = UseAlternateUpdateMethod,
            ShowLatestBetaVersion = ShowLatestBetaVersion,
            ShowAllBetaVersions = ShowAllBetaVersions,
            EnableThriveAutoRestart = EnableThriveAutoRestart,
            PreferSystemTools = PreferSystemTools,
            VerboseLogging = VerboseLogging,
            SelectedDevBuildType = SelectedDevBuildType,
            ManuallySelectedBuildHash = ManuallySelectedBuildHash,
            ForceOpenGlMode = ForceOpenGlMode,
            DisableThriveVideos = DisableThriveVideos,
            DisableThriveMods = DisableThriveMods,
            OverrideAudioLatency = OverrideAudioLatency,
            AudioLatencyMilliseconds = AudioLatencyMilliseconds,
        };
    }

    private static bool GetDefaultHideOnPlayOption()
    {
        // Don't hide by default on Mac: https://github.com/Revolutionary-Games/Thrive-Launcher/issues/159
        if (OperatingSystem.IsMacOS())
            return false;

        return true;
    }
}
