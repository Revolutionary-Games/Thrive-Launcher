namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using Services;
using Utilities;

/// <summary>
///   The primary part of the main window's view model class
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, INoticeDisplayer, IDisposable
{
    private const int TotalKeysInDevCenterActivationSequence = 4;

    private readonly ILogger<MainWindowViewModel> logger;
    private readonly ILauncherFeeds launcherFeeds;
    private readonly IStoreVersionDetector storeInfo;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly VersionUtilities versionUtilities;
    private readonly ILauncherPaths launcherPaths;
    private readonly IThriveAndLauncherInfoRetriever launcherInfoRetriever;
    private readonly IThriveInstaller thriveInstaller;
    private readonly IDevCenterClient devCenterClient;
    private readonly IThriveRunner thriveRunner;
    private readonly ILauncherOptions launcherOptions;
    private readonly IAutoUpdater autoUpdater;
    private readonly IBackgroundExceptionNoticeDisplayer backgroundExceptionNoticeDisplayer;
    private readonly ILoggingManager loggingManager;

    private readonly Dictionary<string, CultureInfo> availableLanguages;
    private readonly StoreVersionInfo detectedStore;

    private string noticeMessageText = string.Empty;
    private string noticeMessageTitle = string.Empty;
    private bool canDismissNotice = true;
    private bool registeredForNoticeDisplay;

    private bool showSettingsUpgrade;

    // Thrive versions info
    private Task launcherInformationTask = null!;
    private LauncherThriveInformation? thriveVersionInformation;

    private string? selectedVersionToPlay;

    private bool canLoadCachedVersions;
    private string launcherInfoLoadError = string.Empty;

    private bool versionInfoIsFresh = true;

    // Feeds
    private Task<List<ParsedLauncherFeedItem>> devForumFeedItems = null!;
    private string? devForumFetchError;
    private bool loadingDevForumFeed;

    private Task<List<ParsedLauncherFeedItem>> mainSiteFeedItems = null!;
    private string? mainSiteFetchError;
    private bool loadingMainSiteFeed;

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ILauncherFeeds launcherFeeds,
        IStoreVersionDetector storeInfo, ILauncherSettingsManager settingsManager, VersionUtilities versionUtilities,
        ILauncherPaths launcherPaths, IThriveAndLauncherInfoRetriever launcherInfoRetriever,
        IThriveInstaller thriveInstaller, IDevCenterClient devCenterClient, IThriveRunner thriveRunner,
        ILauncherOptions launcherOptions, IAutoUpdater autoUpdater,
        IBackgroundExceptionNoticeDisplayer backgroundExceptionNoticeDisplayer, ILoggingManager loggingManager,
        bool allowTaskStarts = true)
    {
        this.logger = logger;
        this.launcherFeeds = launcherFeeds;
        this.storeInfo = storeInfo;
        this.settingsManager = settingsManager;
        this.versionUtilities = versionUtilities;
        this.launcherPaths = launcherPaths;
        this.launcherInfoRetriever = launcherInfoRetriever;
        this.thriveInstaller = thriveInstaller;
        this.devCenterClient = devCenterClient;
        this.thriveRunner = thriveRunner;
        this.launcherOptions = launcherOptions;
        this.autoUpdater = autoUpdater;
        this.backgroundExceptionNoticeDisplayer = backgroundExceptionNoticeDisplayer;
        this.loggingManager = loggingManager;

        DevCenterKeyCommand = ReactiveCommand.Create<int>(DevCenterViewActivation);

        availableLanguages = Languages.GetAvailableLanguages();

        languagePlaceHolderIfNotSelected = Languages
            .GetMatchingCultureOrDefault(Languages.GetStartupLanguage(), availableLanguages).NativeName;

        if (allowTaskStarts)
        {
            logger.LogDebug(
                "Stored language placeholder based on startup language to be: {LanguagePlaceHolderIfNotSelected}",
                languagePlaceHolderIfNotSelected);
        }

        showSettingsUpgrade = settingsManager.V1Settings != null;

        detectedStore = storeInfo.Detect();

        if (detectedStore.ShouldPreventDefaultDevCenterVisibility)
            ShowDevCenterStatusArea = false;

        if (!string.IsNullOrEmpty(Settings.DevCenterKey) && allowTaskStarts)
        {
            // DevCenter visibility when already configured
            ShowDevCenterStatusArea = true;

            CheckDevCenterConnection();
        }

        CreateSettingsTabTasks();

        CreateFeedRetrieveTasks();

        if (Settings.ShowWebContent && allowTaskStarts)
        {
            StartFeedFetch();
        }

        CreateLauncherInfoRetrieveTask();

        if ((!detectedStore.IsStoreVersion || settingsManager.Settings.StoreVersionShowExternalVersions) &&
            allowTaskStarts)
        {
            StartLauncherInfoFetch();
        }
        else if (allowTaskStarts)
        {
            // Just show what info we have so far for the store version's available versions
            DoStoreAlternativeToInfoRetrieve();
        }

        // In case the launcher was restarted without the process ending we need to restore some state from the backend
        if (allowTaskStarts)
            DetectPlayingStatusFromBackend();

        backgroundExceptionNoticeDisplayer.RegisterErrorDisplayer(this);
        registeredForNoticeDisplay = true;
    }

    /// <summary>
    ///   Constructor for live preview
    /// </summary>
    public MainWindowViewModel() : this(DesignTimeServices.Services.GetRequiredService<ILogger<MainWindowViewModel>>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherFeeds>(),
        DesignTimeServices.Services.GetRequiredService<IStoreVersionDetector>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherSettingsManager>(),
        DesignTimeServices.Services.GetRequiredService<VersionUtilities>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherPaths>(),
        DesignTimeServices.Services.GetRequiredService<IThriveAndLauncherInfoRetriever>(),
        DesignTimeServices.Services.GetRequiredService<IThriveInstaller>(),
        DesignTimeServices.Services.GetRequiredService<IDevCenterClient>(),
        DesignTimeServices.Services.GetRequiredService<IThriveRunner>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherOptions>(),
        DesignTimeServices.Services.GetRequiredService<IAutoUpdater>(),
        DesignTimeServices.Services.GetRequiredService<IBackgroundExceptionNoticeDisplayer>(),
        DesignTimeServices.Services.GetRequiredService<ILoggingManager>(), false)
    {
        languagePlaceHolderIfNotSelected = string.Empty;
        DevCenterKeyCommand = null!;
    }

    public bool HasNoticeMessage =>
        !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

    public bool CanDismissNotice
    {
        get => HasNoticeMessage && canDismissNotice;
        private set => this.RaiseAndSetIfChanged(ref canDismissNotice, value);
    }

    public bool ShowLinksNotInSteamVersion => !detectedStore.IsSteam;

    // Probably fine to always show this and not only when a store is not detected
    public bool ShowPatreonLink => true;

    public bool RetryVersionInfoDownload { get; set; }

    public bool LoadCachedVersionInfo { get; set; }

    public bool IsStoreVersion => storeInfo.Detect().IsStoreVersion;

    public string NoticeMessageText
    {
        get => noticeMessageText;
        private set
        {
            if (noticeMessageText == value)
                return;

            this.RaiseAndSetIfChanged(ref noticeMessageText, value);
            this.RaisePropertyChanged(nameof(HasNoticeMessage));
            this.RaisePropertyChanged(nameof(CanDismissNotice));
        }
    }

    public string NoticeMessageTitle
    {
        get => noticeMessageTitle;
        private set
        {
            if (noticeMessageTitle == value)
                return;

            this.RaiseAndSetIfChanged(ref noticeMessageTitle, value);
            this.RaisePropertyChanged(nameof(HasNoticeMessage));
            this.RaisePropertyChanged(nameof(CanDismissNotice));
        }
    }

    public bool CanLoadCachedVersions
    {
        get => canLoadCachedVersions;
        private set => this.RaiseAndSetIfChanged(ref canLoadCachedVersions, value);
    }

    public string LauncherInfoLoadError
    {
        get => launcherInfoLoadError;
        private set => this.RaiseAndSetIfChanged(ref launcherInfoLoadError, value);
    }

    public LauncherThriveInformation? ThriveVersionInformation
    {
        get => thriveVersionInformation;
        private set
        {
            if (value == thriveVersionInformation)
                return;

            this.RaiseAndSetIfChanged(ref thriveVersionInformation, value);
            this.RaisePropertyChanged(nameof(AvailableThriveVersions));

            if (value != null)
                OnVersionInfoLoaded();
        }
    }

    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> AvailableThriveVersions =>
        thriveInstaller.GetAvailableThriveVersions();

    public string? SelectedVersionToPlay
    {
        get => selectedVersionToPlay;
        set
        {
            if (selectedVersionToPlay == value)
                return;

            this.RaiseAndSetIfChanged(ref selectedVersionToPlay, value);
            this.RaisePropertyChanged(nameof(CanPressPlayButton));
        }
    }

    public string? DevForumFetchError
    {
        get => devForumFetchError;
        private set => this.RaiseAndSetIfChanged(ref devForumFetchError, value);
    }

    public Task<List<ParsedLauncherFeedItem>> DevForumFeedItems => devForumFeedItems;

    public bool LoadingDevForumFeed
    {
        get => loadingDevForumFeed;
        private set => this.RaiseAndSetIfChanged(ref loadingDevForumFeed, value);
    }

    public string? MainSiteFetchError
    {
        get => mainSiteFetchError;
        private set => this.RaiseAndSetIfChanged(ref mainSiteFetchError, value);
    }

    public bool LoadingMainSiteFeed
    {
        get => loadingMainSiteFeed;
        private set => this.RaiseAndSetIfChanged(ref loadingMainSiteFeed, value);
    }

    public Task<List<ParsedLauncherFeedItem>> MainSiteFeedItems => mainSiteFeedItems;

    public bool ShowSettingsUpgrade
    {
        get => showSettingsUpgrade;
        private set => this.RaiseAndSetIfChanged(ref showSettingsUpgrade, value);
    }

    /// <summary>
    ///   Call to shutdown all event handling listeners that this view model may be using
    /// </summary>
    public void ShutdownListeners()
    {
        UnRegisterThriveRunnerListeners();
        UnRegisterInstallerMessageForwarders();
        UnRegisterAutoUpdaterCallbacks();

        if (registeredForNoticeDisplay)
        {
            if (!backgroundExceptionNoticeDisplayer.RemoveErrorDisplayer(this))
            {
                logger.LogWarning("Could not unregister this window from showing background errors");
            }

            registeredForNoticeDisplay = false;
        }
    }

    public IEnumerable<string> GetAvailableLanguages()
    {
        return availableLanguages.Keys;
    }

    public void ShowNotice(string title, string text, bool canDismiss = true)
    {
        Dispatcher.UIThread.Post(() =>
        {
            NoticeMessageTitle = title;
            NoticeMessageText = text;
            CanDismissNotice = canDismiss;
        });
    }

    public void VersionSelected(string? userReadableVersion)
    {
        if (string.IsNullOrEmpty(userReadableVersion))
        {
            SelectedVersionToPlay = null;
            return;
        }

        // Convert the user-readable version to a normal version
        var version = AvailableThriveVersions.First(t => t.VersionObject.VersionName == userReadableVersion);

        logger.LogInformation("Version to play is now: {SelectedVersion}", userReadableVersion);

        SelectedVersionToPlay = userReadableVersion;

        // When selecting the latest version, we want to clear the remembered version so the user always gets the
        // latest version to play.
        // Or if playing a store version that also clears the remembered version
        if (version.VersionObject is PlayableVersion { IsLatest: true } or StoreVersion)
        {
            logger.LogInformation("Select version to play is latest (or store version), clearing remembered version");
            settingsManager.RememberedVersion = null;
        }
        else
        {
            settingsManager.RememberedVersion = version.VersionName;
        }
    }

    public void SetRetryVersionInfoDownload()
    {
        // Clearing this makes the dialog disappear so the user at least gets some feedback that something is happening
        LauncherInfoLoadError = string.Empty;

        RetryVersionInfoDownload = true;
    }

    /// <summary>
    ///   Configures the launcher to attempt to load the cached Thrive version info file on the next loop
    /// </summary>
    public void SetLoadCachedVersionInfo()
    {
        LauncherInfoLoadError = string.Empty;

        LoadCachedVersionInfo = true;
    }

    public void CloseNotice()
    {
        NoticeMessageText = string.Empty;
        NoticeMessageTitle = string.Empty;
    }

    public void DismissOutdatedMessage()
    {
        LauncherOutdatedVersionMessage = string.Empty;
    }

    public void SkipSettingsUpgrade()
    {
        ShowSettingsUpgrade = false;
    }

    public async Task PerformSettingsUpgrade()
    {
        var result = await settingsManager.ImportOldSettings();

        ShowSettingsUpgrade = false;

        if (!result)
        {
            ShowNotice(Resources.ImportFailedTitle, Resources.ImportFailedMessage);
        }
        else
        {
            ShowNotice(Resources.ImportSucceededTitle, Resources.ImportSucceededMessage);

            // In case there's a DevCenter connection, start checking that again
            CheckDevCenterConnection();
        }
    }

    public void OnUserRequestLauncherClose()
    {
        logger.LogInformation("User has requested launcher to quit now");
        LauncherShouldClose = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }

    private void CreateFeedRetrieveTasks()
    {
        devForumFeedItems = new Task<List<ParsedLauncherFeedItem>>(() =>
            FetchFeed("DevForum", LauncherConstants.DevForumFeedURL, false).Result);

        mainSiteFeedItems = new Task<List<ParsedLauncherFeedItem>>(() =>
            FetchFeed("MainSite", LauncherConstants.MainSiteFeedURL, true).Result);

        // We don't start the tasks here to ensure that no network requests are done if web content is turned off
        // in settings
    }

    private void StartFeedFetch()
    {
        if (devForumFeedItems.Status == TaskStatus.Created)
            devForumFeedItems.Start();

        if (mainSiteFeedItems.Status == TaskStatus.Created)
            mainSiteFeedItems.Start();
    }

    private void CreateLauncherInfoRetrieveTask()
    {
        launcherInformationTask = new Task(PerformLauncherInfoRetrieve);
    }

    private async void PerformLauncherInfoRetrieve()
    {
        var launcherInfo = await FetchLauncherInfo();

        if (launcherInfo == null)
            return;

        try
        {
            logger.LogInformation(
                "Version information loaded. Thrive versions: {Versions}, latest launcher: {LatestVersion}",
                launcherInfo.Versions.Count, launcherInfo.LauncherVersion.LatestVersion);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Got bad launcher info data");
            ShowNotice(Resources.VersionInfoLoadFailureTitle, Resources.VersionInfoLoadFailureBadData);
            return;
        }

        await WaitForDevCenterConnection();

        Dispatcher.UIThread.Post(() =>
        {
            // We now have the version info to work with
            ThriveVersionInformation = launcherInfo;

            CheckLauncherVersion(launcherInfo);
        });
    }

    /// <summary>
    ///   Alternative operations when a store version is used that doesn't have external versions enabled to
    ///   <see cref="PerformLauncherInfoRetrieve"/>
    /// </summary>
    private void DoStoreAlternativeToInfoRetrieve()
    {
        // ReSharper disable once MethodSupportsCancellation
        backgroundExceptionNoticeDisplayer.HandleTask(Task.Run(async () =>
        {
            logger.LogInformation("Starting as launcher store version with external versions disabled");

            await WaitForDevCenterConnection();

            LoadDummyStoreVersionData();
        }));
    }

    private void LoadDummyStoreVersionData()
    {
        Dispatcher.UIThread.Post(() =>
        {
            versionInfoIsFresh = false;
            launcherInfoRetriever.ForgetInfo();

            // We use dummy version info here to get rid of the loading screen in the launcher for store versions
            ThriveVersionInformation = new LauncherThriveInformation(
                new LauncherVersionInfo(versionUtilities.AssemblyVersion.ToString()), -1,
                new List<ThriveVersionLauncherInfo>(), new Dictionary<string, DownloadMirrorInfo>());
        });
    }

    private async Task WaitForDevCenterConnection()
    {
        // Wait for DevCenter connection task if currently running
        int timeout = 30;
        while (CheckingDevCenterConnection)
        {
            // ReSharper disable MethodSupportsCancellation
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            // ReSharper restore MethodSupportsCancellation
            logger.LogDebug("Waiting for DevCenter status check request to complete...");

            --timeout;

            if (timeout < 1)
            {
                logger.LogWarning("Timing out waiting for a DevCenter connection");
                break;
            }
        }
    }

    private void StartLauncherInfoFetch()
    {
        if (launcherInformationTask.Status == TaskStatus.Created)
        {
            logger.LogDebug("Starting fetch of launcher info...");
            launcherInformationTask.Start();
        }
        else if (IsStoreVersion)
        {
            // If the user toggles between the external versions option in the store version,
            // we need to do this kind of thing here to get things back
            launcherInfoRetriever.RestoreBackupInfo();

            // We don't set versionInfoIsFresh to true here as we don't know if the backed up info was fresh or not
            // so we just skip that as it isn't super important here

            Dispatcher.UIThread.Post(() => { ThriveVersionInformation = launcherInfoRetriever.CurrentlyLoadedInfo; });
        }
    }

    private void OnVersionInfoLoaded()
    {
        SetRememberedVersionToVersionSelector();

        // Only check for no available versions once the version data is loaded, otherwise this could trigger too early
        // (as this used to be in SetRememberedVersionToVersionSelector)
        if (!AvailableThriveVersions.Any())
        {
            logger.LogWarning(
                "Could not detect any Thrive versions that are playable (compatible with current platform)");
            ShowNotice(Resources.NoCompatibleVersionsFoundTitle, Resources.NoCompatibleVersionsFound);
        }
    }

    private void SetRememberedVersionToVersionSelector()
    {
        var remembered = settingsManager.RememberedVersion;

        var availableThings = AvailableThriveVersions.ToList();

        if (availableThings.Count < 1)
        {
            logger.LogInformation("No detected versions, can't set a remembered version");
            return;
        }

        if (remembered != null)
        {
            // Set the selected item to a remembered one
            var foundRememberedVersion = availableThings.Where(t => t.VersionName == remembered)
                .Select(t => t.VersionObject).FirstOrDefault();
            if (foundRememberedVersion != null)
            {
                SelectedVersionToPlay = foundRememberedVersion.VersionName;
                logger.LogInformation("Remembered version ({Remembered}) set to selector", remembered);
                return;
            }
        }

        // If there is a store version, pick that
        var storeVersion = availableThings.Where(t => t.VersionObject is StoreVersion).Select(t => t.VersionObject)
            .FirstOrDefault();

        if (storeVersion != null)
        {
            logger.LogInformation("Auto selecting store version");
            SelectedVersionToPlay = storeVersion.VersionName;
            return;
        }

        // Otherwise just select the latest version
        try
        {
            SelectedVersionToPlay = availableThings.Where(t => t.VersionObject is PlayableVersion)
                .First(t => ((PlayableVersion)t.VersionObject).IsLatest).VersionObject.VersionName;
        }
        catch (InvalidOperationException e)
        {
            logger.LogDebug(e, "Cannot select latest version as it is probably not available for this platform");

            // Instead select latest version that is available
            // TODO: add smarter version based sorting if necessary
            SelectedVersionToPlay = availableThings.Where(t => t.VersionObject is PlayableVersion)
                .Reverse().Select(t => (PlayableVersion)t.VersionObject).First().VersionName;
        }
    }

    private void NotifyChangesToAvailableVersions()
    {
        this.RaisePropertyChanged(nameof(AvailableThriveVersions));

        SetRememberedVersionToVersionSelector();
    }

    private async Task<List<ParsedLauncherFeedItem>> FetchFeed(string name, Uri uri, bool mainSite)
    {
        NotifyFeedLoadingState(mainSite, true);

        logger.LogDebug("Fetching feed {Name}", name);

        var (failure, data) = await launcherFeeds.FetchFeed(name, uri);

        NotifyFeedLoadingState(mainSite, false);

        if (failure != null)
        {
            SetFetchError(mainSite, failure);
            return new List<ParsedLauncherFeedItem>();
        }

        return data!;
    }

    private void NotifyFeedLoadingState(bool mainSite, bool loading)
    {
        if (mainSite)
        {
            Dispatcher.UIThread.Post(() => LoadingMainSiteFeed = loading);
        }
        else
        {
            Dispatcher.UIThread.Post(() => LoadingDevForumFeed = loading);
        }
    }

    private void SetFetchError(bool mainSite, string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var fullText = string.Format(Resources.FeedFetchError, error);

            if (mainSite)
            {
                MainSiteFetchError = fullText;
            }
            else
            {
                DevForumFetchError = fullText;
            }
        });
    }

    private async Task<LauncherThriveInformation?> FetchLauncherInfo()
    {
        while (true)
        {
            try
            {
                if (LoadCachedVersionInfo)
                {
                    LoadCachedVersionInfo = false;
                    versionInfoIsFresh = false;
                    var result = await launcherInfoRetriever.LoadFromCache();

                    if (result == null)
                        throw new Exception("Loading cached file failed");

                    return result;
                }

                logger.LogInformation("Fetching Thrive launcher info");
                RetryVersionInfoDownload = false;
                versionInfoIsFresh = true;
                return await launcherInfoRetriever.DownloadInfo();
            }
            catch (AllKeysExpiredException e)
            {
                logger.LogError(e, "All our checking keys have expired. PLEASE UPDATE THE LAUNCHER!");

                // For the launcher we assume that people can always update (and for example people never want to use an
                // older version like some will want to do with Thrive itself)
                ShowNotice(Resources.AllSigningKeysExpiredTitle, Resources.AllSigningKeysExpired, false);
                return null;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to retrieve launcher Thrive version info");

                Dispatcher.UIThread.Post(() =>
                {
                    CanLoadCachedVersions = launcherInfoRetriever.HasCachedFile();
                    LauncherInfoLoadError = string.IsNullOrEmpty(e.Message) ? Resources.UnknownError : e.Message;
                });

                // Not the best design here, but this seems good enough here, this situation shouldn't really be hit
                // by most users ever. As a slight benefit the one second delay here kind of rate limits the user
                // from spamming the remote server too much if it is down.
                while (!LoadCachedVersionInfo && !RetryVersionInfoDownload)
                {
                    // ReSharper disable once MethodSupportsCancellation
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                logger.LogInformation("Retrying or using cached data next");
            }
        }
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            ShutdownListeners();

            launcherInformationTask.Dispose();
            devForumFeedItems.Dispose();
            mainSiteFeedItems.Dispose();
            dehydrateCacheSizeTask?.Dispose();
            autoUpdateCancellation.Dispose();
            playActionCancellationSource?.Dispose();
        }
    }
}
