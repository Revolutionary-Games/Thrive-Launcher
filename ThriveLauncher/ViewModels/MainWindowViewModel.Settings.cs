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
    private LauncherSettings Settings => settingsManager.Settings;

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
                StartLauncherInfoFetch();
        }
    }

    public bool AutoStartStoreVersion
    {
        get => Settings.AutoStartStoreVersion;
        set
        {
            if (Settings.AutoStartStoreVersion == value)
                return;

            this.RaisePropertyChanging();
            Settings.AutoStartStoreVersion = value;
            this.RaisePropertyChanged();
        }
    }

    public string SelectedLauncherLanguage
    {
        get => Settings.SelectedLauncherLanguage ?? languagePlaceHolderIfNotSelected;
        set
        {
            if (Settings.SelectedLauncherLanguage == value)
                return;

            this.RaisePropertyChanging();
            Settings.SelectedLauncherLanguage = value;
            Languages.SetLanguage(availableLanguages[SelectedLauncherLanguage]);
            this.RaisePropertyChanged();
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

            this.RaisePropertyChanging();
            Settings.TemporaryDownloadsFolder = value;
            this.RaisePropertyChanged();
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
        get => Settings.SelectedDevBuildType;
        set
        {
            if (Settings.SelectedDevBuildType == value)
                return;

            this.RaisePropertyChanging();
            Settings.SelectedDevBuildType = value;
            this.RaisePropertyChanged();
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
            this.RaisePropertyChanged();
        }
    }

    public bool ForceGles2Mode
    {
        get => Settings.ForceGles2Mode;
        set
        {
            if (Settings.ForceGles2Mode == value)
                return;

            this.RaisePropertyChanging();
            Settings.ForceGles2Mode = value;
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


}
