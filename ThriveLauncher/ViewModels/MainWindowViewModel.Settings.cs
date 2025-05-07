namespace ThriveLauncher.ViewModels;

using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   The <see cref="LauncherSettings"/> proxy parts for the view model
/// </summary>
public partial class MainWindowViewModel
{
    private readonly string languagePlaceHolderIfNotSelected;

    public bool ShowWebContent
    {
        get => Settings.ShowWebContent;
        set
        {
            if (Settings.ShowWebContent == value)
                return;

            this.RaisePropertyChanging();
            Settings.ShowWebContent = value;
            this.RaisePropertyChanged();

            if (ShowWebContent)
            {
                // Start fetching web content if not fetched already
                StartFeedFetch();
            }
        }
    }

    public bool HideLauncherOnPlay
    {
        get => Settings.HideLauncherOnPlay;
        set
        {
            if (Settings.HideLauncherOnPlay == value)
                return;

            this.RaisePropertyChanging();
            Settings.HideLauncherOnPlay = value;
            this.RaisePropertyChanged();
        }
    }

    public bool Hide32BitVersions
    {
        get => Settings.Hide32Bit;
        set
        {
            if (Settings.Hide32Bit == value)
                return;

            this.RaisePropertyChanging();
            Settings.Hide32Bit = value;
            this.RaisePropertyChanged();

            NotifyChangesToAvailableVersions();
        }
    }

    public bool CloseLauncherAfterGameExit
    {
        get => Settings.CloseLauncherAfterGameExit;
        set
        {
            if (Settings.CloseLauncherAfterGameExit == value)
                return;

            this.RaisePropertyChanging();
            Settings.CloseLauncherAfterGameExit = value;
            this.RaisePropertyChanged();
        }
    }

    public bool CloseLauncherOnGameStart
    {
        get => Settings.CloseLauncherOnGameStart;
        set
        {
            if (Settings.CloseLauncherOnGameStart == value)
                return;

            this.RaisePropertyChanging();
            Settings.CloseLauncherOnGameStart = value;
            this.RaisePropertyChanged();
        }
    }

    public bool StoreVersionShowExternalVersions
    {
        get => Settings.StoreVersionShowExternalVersions;
        set
        {
            if (Settings.StoreVersionShowExternalVersions == value)
                return;

            this.RaisePropertyChanging();
            Settings.StoreVersionShowExternalVersions = value;
            this.RaisePropertyChanged();

            if (StoreVersionShowExternalVersions)
            {
                StartLauncherInfoFetch();
            }
            else
            {
                // Remove the versions from the play selector
                logger.LogInformation("Forgetting loaded launcher info if there is any");
                LoadDummyStoreVersionData();
            }

            this.RaisePropertyChanged(nameof(CanEnableShowingBetaVersions));
            this.RaisePropertyChanged(nameof(CanShowLatestBetaVersion));
        }
    }

    public bool EnableStoreVersionSeamlessMode
    {
        get => Settings.EnableStoreVersionSeamlessMode;
        set
        {
            if (Settings.EnableStoreVersionSeamlessMode == value)
                return;

            this.RaisePropertyChanging();
            Settings.EnableStoreVersionSeamlessMode = value;
            this.RaisePropertyChanged();
        }
    }

    public string SelectedLauncherLanguage
    {
        get => Settings.SelectedLauncherLanguage ?? languagePlaceHolderIfNotSelected;
        set
        {
            // When setting the user's default language, use null
            string? languageToSave = value;
            if (languagePlaceHolderIfNotSelected == value)
            {
                logger.LogDebug("Replacing default language value with null");
                languageToSave = null;
            }

            if (Settings.SelectedLauncherLanguage == languageToSave)
            {
                logger.LogDebug("Language changed to language that is currently set, ignoring");
                return;
            }

            logger.LogInformation("Launcher language is changing to: {Value}", value);

            this.RaisePropertyChanging();
            Settings.SelectedLauncherLanguage = languageToSave;
            Languages.SetLanguage(availableLanguages[SelectedLauncherLanguage]);
            this.RaisePropertyChanged();

            // Language affects the names in the version selector
            NotifyChangesToAvailableVersions();
        }
    }

    public string ThriveInstallationPath
    {
        get => Settings.ThriveInstallationPath ?? launcherPaths.PathToDefaultThriveInstallFolder;
        set
        {
            if (Settings.ThriveInstallationPath == value)
                return;

            this.RaisePropertyChanging();
            Settings.ThriveInstallationPath = value;
            this.RaisePropertyChanged();
        }
    }

    public string DehydratedCacheFolder
    {
        get => Settings.DehydratedCacheFolder ?? launcherPaths.PathToDefaultDehydrateCacheFolder;
        set
        {
            if (Settings.DehydratedCacheFolder == value)
                return;

            this.RaisePropertyChanging();
            Settings.DehydratedCacheFolder = value;
            this.RaisePropertyChanged();

            RefreshDehydratedCacheSize();
        }
    }

    public string TemporaryDownloadsFolder
    {
        get => Settings.TemporaryDownloadsFolder ?? launcherPaths.PathToTemporaryFolder;
        set
        {
            if (Settings.TemporaryDownloadsFolder == value)
                return;

            SetTemporaryLocationTo(value);
        }
    }

    public bool AutoCleanTemporaryFolder
    {
        get => Settings.AutoCleanTemporaryFolder;
        set
        {
            if (Settings.AutoCleanTemporaryFolder == value)
                return;

            this.RaisePropertyChanging();
            Settings.AutoCleanTemporaryFolder = value;
            this.RaisePropertyChanged();
        }
    }

    public bool AllowAutoUpdate
    {
        get => Settings.AllowAutoUpdate;
        set
        {
            if (Settings.AllowAutoUpdate == value)
                return;

            this.RaisePropertyChanging();
            Settings.AllowAutoUpdate = value;
            this.RaisePropertyChanged();
        }
    }

    public bool UseAlternateUpdateMethod
    {
        get => Settings.UseAlternateUpdateMethod;
        set
        {
            if (Settings.UseAlternateUpdateMethod == value)
                return;

            this.RaisePropertyChanging();
            Settings.UseAlternateUpdateMethod = value;
            this.RaisePropertyChanged();
        }
    }

    public bool ShowLatestBetaVersion
    {
        get => Settings.ShowLatestBetaVersion;
        set
        {
            if (Settings.ShowLatestBetaVersion == value)
                return;

            this.RaisePropertyChanging();
            Settings.ShowLatestBetaVersion = value;
            this.RaisePropertyChanged();

            this.RaisePropertyChanged(nameof(CanShowLatestBetaVersion));

            NotifyChangesToAvailableVersions();
        }
    }

    public bool ShowAllBetaVersions
    {
        get => Settings.ShowAllBetaVersions;
        set
        {
            if (Settings.ShowAllBetaVersions == value)
                return;

            this.RaisePropertyChanging();
            Settings.ShowAllBetaVersions = value;
            this.RaisePropertyChanged();

            NotifyChangesToAvailableVersions();
        }
    }

    public bool EnableThriveAutoRestart
    {
        get => Settings.EnableThriveAutoRestart;
        set
        {
            if (Settings.EnableThriveAutoRestart == value)
                return;

            this.RaisePropertyChanging();
            Settings.EnableThriveAutoRestart = value;
            this.RaisePropertyChanged();
        }
    }

    public bool VerboseLogging
    {
        get => Settings.VerboseLogging;
        set
        {
            if (Settings.VerboseLogging == value)
                return;

            this.RaisePropertyChanging();
            Settings.VerboseLogging = value;
            this.RaisePropertyChanged();

            logger.LogInformation("Setting verbose logging option to: {VerboseLogging}", Settings.VerboseLogging);
            loggingManager.ApplyVerbosityOption(Settings.VerboseLogging);
        }
    }

    public string? DevCenterKey
    {
        get => Settings.DevCenterKey;
        set
        {
            if (Settings.DevCenterKey == value)
                return;

            this.RaisePropertyChanging();
            Settings.DevCenterKey = value;
            this.RaisePropertyChanged();
        }
    }

    public DevBuildType SelectedDevBuildType
    {
        get => Settings.SelectedDevBuildType ?? DevBuildType.BuildOfTheDay;
        set
        {
            if (Settings.SelectedDevBuildType == value)
                return;

            this.RaisePropertyChanging();
            Settings.SelectedDevBuildType = value;

            // Also change the type of thing to play to devbuild automatically to avoid user errors
            // But only if connected to the DevCenter to not show a nonsensical value
            if (DevCenterConnection != null)
            {
                const string devbuildName = "DevBuild";

                if (SelectedVersionToPlay != devbuildName)
                {
                    logger.LogInformation("Automatically selecting 'DevBuild' to play as DevBuild type was changed");

                    SelectedVersionToPlay = devbuildName;

                    // Remember this change across restarts
                    settingsManager.RememberedVersion = devbuildName;
                }
            }

            this.RaisePropertyChanged();

            this.RaisePropertyChanged(nameof(SelectedDevBuildTypeIsBuildOfTheDay));
            this.RaisePropertyChanged(nameof(SelectedDevBuildTypeIsLatest));
            this.RaisePropertyChanged(nameof(SelectedDevBuildTypeIsManuallySelected));
        }
    }

    public string? ManuallySelectedBuildHash
    {
        get => Settings.ManuallySelectedBuildHash;
        set
        {
            if (Settings.ManuallySelectedBuildHash == value)
                return;

            this.RaisePropertyChanging();
            Settings.ManuallySelectedBuildHash = value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                // Swap the DevBuild type to "manual" automatically to avoid user-errors in playing the wrong thing
                SelectedDevBuildType = DevBuildType.ManuallySelected;
            }

            this.RaisePropertyChanged();
        }
    }

    public bool ForceOpenGlMode
    {
        get => Settings.ForceOpenGlMode;
        set
        {
            if (Settings.ForceOpenGlMode == value)
                return;

            this.RaisePropertyChanging();
            Settings.ForceOpenGlMode = value;
            this.RaisePropertyChanged();
        }
    }

    public bool DisableThriveVideos
    {
        get => Settings.DisableThriveVideos;
        set
        {
            if (Settings.DisableThriveVideos == value)
                return;

            this.RaisePropertyChanging();
            Settings.DisableThriveVideos = value;
            this.RaisePropertyChanged();
        }
    }

    public bool OverrideThriveAudioLatency
    {
        get => Settings.OverrideAudioLatency;
        set
        {
            if (Settings.OverrideAudioLatency == value)
                return;

            this.RaisePropertyChanging();
            Settings.OverrideAudioLatency = value;
            this.RaisePropertyChanged();
        }
    }

    public int ThriveAudioLatencyMilliseconds
    {
        get => Settings.AudioLatencyMilliseconds;
        set
        {
            if (Settings.AudioLatencyMilliseconds == value)
                return;

            this.RaisePropertyChanging();
            Settings.AudioLatencyMilliseconds = value;
            this.RaisePropertyChanged();
        }
    }

    // Derived setting values

    public bool SelectedDevBuildTypeIsBuildOfTheDay
    {
        get => SelectedDevBuildType == DevBuildType.BuildOfTheDay;
        set
        {
            if (value == false)
            {
                // We can't really unset this here, so we do nothing...
                return;
            }

            SelectedDevBuildType = DevBuildType.BuildOfTheDay;
        }
    }

    public bool SelectedDevBuildTypeIsLatest
    {
        get => SelectedDevBuildType == DevBuildType.Latest;
        set
        {
            if (value == false)
                return;

            SelectedDevBuildType = DevBuildType.Latest;
        }
    }

    public bool SelectedDevBuildTypeIsManuallySelected
    {
        get => SelectedDevBuildType == DevBuildType.ManuallySelected;
        set
        {
            if (value == false)
                return;

            SelectedDevBuildType = DevBuildType.ManuallySelected;
        }
    }

    public bool CanEnableShowingBetaVersions => StoreVersionShowExternalVersions || !detectedStore.IsStoreVersion;
    public bool CanShowLatestBetaVersion => CanEnableShowingBetaVersions && ShowLatestBetaVersion;

    private LauncherSettings Settings => settingsManager.Settings;
}
