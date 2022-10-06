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
using Utilities;

public partial class MainWindowViewModel : ViewModelBase
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

    private readonly Dictionary<string, CultureInfo> availableLanguages;
    private readonly StoreVersionInfo detectedStore;

    private string noticeMessageText = string.Empty;
    private string noticeMessageTitle = string.Empty;
    private bool canDismissNotice = true;

    private bool showSettingsUpgrade;

    // Thrive versions info
    private Task launcherInformationTask = null!;
    private LauncherThriveInformation? thriveVersionInformation;

    private string? selectedVersionToPlay;

    private bool canLoadCachedVersions;
    private string launcherInfoLoadError = string.Empty;

    private bool versionInfoIsFresh = true;

    private bool launcherIsLatestVersion;
    private string launcherOutdatedVersionMessage = string.Empty;

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
        IThriveInstaller thriveInstaller, IDevCenterClient devCenterClient,
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

        availableLanguages = Languages.GetAvailableLanguages();

        languagePlaceHolderIfNotSelected = Languages.GetCurrentlyUsedCulture(availableLanguages).NativeName;

        showSettingsUpgrade = settingsManager.V1Settings != null;

        detectedStore = storeInfo.Detect();

        if (detectedStore.ShouldPreventDefaultDevCenterVisibility)
            ShowDevCenterStatusArea = false;

        if (!string.IsNullOrEmpty(Settings.DevCenterKey))
        {
            // DevCenter visibility when already configured
            ShowDevCenterStatusArea = true;

            // TODO: start checking if devcenter connection is fine
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
        DesignTimeServices.Services.GetRequiredService<IDevCenterClient>(), false)
    {
        languagePlaceHolderIfNotSelected = string.Empty;
    }

    public bool HasNoticeMessage =>
        !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

    public string LauncherVersion => versionUtilities.LauncherVersion + LauncherConstants.ModeSuffix;

    public bool CanDismissNotice
    {
        get => HasNoticeMessage && canDismissNotice;
        private set => this.RaiseAndSetIfChanged(ref canDismissNotice, value);
    }

    public bool ShowLinksNotInSteamVersion => !detectedStore.IsSteam;

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
        private set
        {
            this.RaiseAndSetIfChanged(ref canLoadCachedVersions, value);
        }
    }

    public string LauncherInfoLoadError
    {
        get => launcherInfoLoadError;
        private set
        {
            this.RaiseAndSetIfChanged(ref launcherInfoLoadError, value);
        }
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
        set => this.RaiseAndSetIfChanged(ref selectedVersionToPlay, value);
    }

    public bool LauncherIsLatestVersion
    {
        get => launcherIsLatestVersion;
        private set => this.RaiseAndSetIfChanged(ref launcherIsLatestVersion, value);
    }

    public string LauncherOutdatedVersionMessage
    {
        get => launcherOutdatedVersionMessage;
        private set => this.RaiseAndSetIfChanged(ref launcherOutdatedVersionMessage, value);
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

    public IEnumerable<string> GetAvailableLanguages()
    {
        return availableLanguages.Keys;
    }

    public void ShowNotice(string title, string text, bool canDismiss = true)
    {
        NoticeMessageTitle = title;
        NoticeMessageText = text;
        CanDismissNotice = canDismiss;
    }

    public void VersionSelected(string? userReadableVersion)
    {
        if (string.IsNullOrEmpty(userReadableVersion))
        {
            SelectedVersionToPlay = null;
            return;
        }

        // Convert the user readable version to a normal version
        var version = AvailableThriveVersions.First(t => t.VersionObject.VersionName == userReadableVersion);

        logger.LogInformation("Version to play is now: {SelectedVersion}", userReadableVersion);

        SelectedVersionToPlay = userReadableVersion;

        // When selecting the latest version, we want to clear the remembered version so the user always gets the
        // latest version to play
        if (version.VersionObject is PlayableVersion { IsLatest: true })
        {
            logger.LogInformation("Select version to play is latest, clearing remembered version");
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
        }
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
        launcherInformationTask = new Task(() =>
        {
            var launcherInfo = FetchLauncherInfo().Result;

            if (launcherInfo != null)
            {
                logger.LogInformation(
                    "Version information loaded. Thrive versions: {Versions}, latest launcher: {LatestVersion}",
                    launcherInfo.Versions.Count, launcherInfo.LauncherVersion.LatestVersion);

                Dispatcher.UIThread.Post(() =>
                {
                    // TODO: wait for devcenter connection task if currently running

                    // We now have the version info to work with
                    ThriveVersionInformation = launcherInfo;

                    CheckLauncherVersion(launcherInfo);
                });
            }
        });
    }

    private void StartLauncherInfoFetch()
    {
        if (launcherInformationTask.Status == TaskStatus.Created)
            launcherInformationTask.Start();
    }

    private void OnVersionInfoLoaded()
    {
        var remembered = settingsManager.RememberedVersion;

        var availableThings = AvailableThriveVersions.ToList();

        if (availableThings.Count < 1)
        {
            logger.LogWarning("Could not detect any Thrive versions compatible with current platform");
            ShowNotice(Resources.NoCompatibleVersionsFoundTitle, Resources.NoCompatibleVersionsFound);
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
        SelectedVersionToPlay = availableThings.Where(t => t.VersionObject is PlayableVersion)
            .First(t => ((PlayableVersion)t.VersionObject).IsLatest).VersionObject.VersionName;
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
                Dispatcher.UIThread.Post(() =>
                {
                    ShowNotice(Resources.AllSigningKeysExpiredTitle, Resources.AllSigningKeysExpired, false);
                });
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
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                logger.LogInformation("Retrying or using cached data next");
            }
        }
    }

    private void CheckLauncherVersion(LauncherThriveInformation launcherInfo)
    {
        // Store versions are not updated through the launcher
        if (detectedStore.IsStoreVersion)
        {
            logger.LogDebug("We are a store version, not checking launcher updates");
            return;
        }

        // TODO: flatpak version also needs to skip this

        // TODO: remove, just for testing
        launcherInfo.LauncherVersion = new LauncherVersionInfo("2.0.1");

        var current = versionUtilities.AssemblyVersion;

        // TODO: show if the current launcher is up to date in the options menu (as long as we didn't load cached info)

        if (!Version.TryParse(launcherInfo.LauncherVersion.LatestVersion, out var latest))
        {
            logger.LogError("Cannot check if we are the latest launcher version due to error");
            return;
        }

        if (versionInfoIsFresh)
        {
            LauncherIsLatestVersion = true;
        }

        if (current.Equals(latest))
        {
            logger.LogInformation("We are using the latest launcher version: {Latest}", latest);
        }
        else if (current > latest)
        {
            logger.LogInformation("We are using a newer launcher than is available {Current} > {Latest}", current,
                latest);
        }
        else if (current < latest)
        {
            logger.LogInformation("We are not the latest launcher version, {Current} < {Latest}", current, latest);

            LauncherOutdatedVersionMessage = string.Format(Resources.OutdatedLauncherVersionComparison, versionUtilities.LauncherVersion, latest);

            LauncherIsLatestVersion = false;

            /*

            const link = document.createElement("a");
            link.textContent = "Visit releases page";

            const urlTarget = dlVersion.releaseDLURL ||
                "https://github.com/Revolutionary-Games/Thrive-Launcher/releases";
            link.href = urlTarget;

            message.append(document.createElement("br"));
            message.append(link);

            const textParent = $("#newReleaseAvailableText");

            textParent.empty();
            textParent.append($(message));

            // Buttons //
            const container = document.createElement("div");

            container.classList.add("UpdateButtonContainer");

            const dlNow = document.createElement("div");
            dlNow.classList.add("BottomButton");
            dlNow.style.fontSize = "3.4em";

            dlNow.textContent = "Download Updated Launcher";

            container.append(dlNow);
            textParent.append($(container));


            dlNow.addEventListener("click", () => {

                console.log("Clicked download now");
                require("electron").shell.openExternal(urlTarget);
                dlNow.textContent = "Opening link...";
            });
             */

            bool autoUpdateStarted = false;

            if (AllowAutoUpdate)
            {
                // TODO: trigger auto-update
                logger.LogInformation("Trying to trigger auto-update");
            }

            if (!autoUpdateStarted)
            {
                // Can't trigger auto-update so show outdated heads up
                logger.LogInformation("Auto update not started, showing user we are outdated");
            }
        }
    }
}
