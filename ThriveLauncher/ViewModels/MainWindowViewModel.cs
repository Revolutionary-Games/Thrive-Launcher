namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using ScriptsBase.Utilities;
using SharedBase.Utilities;
using Utilities;
using FileUtilities = LauncherBackend.Utilities.FileUtilities;

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

    // Feeds
    private Task<List<ParsedLauncherFeedItem>> devForumFeedItems = null!;
    private string? devForumFetchError;
    private bool loadingDevForumFeed;

    private Task<List<ParsedLauncherFeedItem>> mainSiteFeedItems = null!;
    private string? mainSiteFetchError;
    private bool loadingMainSiteFeed;

    // Settings sub view
    private bool showSettingsPopup;

    private Task<string>? dehydrateCacheSizeTask;

    private IEnumerable<FolderInInstallFolder>? installedFolders;

    // Settings related file moving
    private bool hasPendingFileMoveOffer;
    private string fileMoveOfferTitle = string.Empty;
    private string fileMoveOfferContent = string.Empty;
    private string fileMoveOfferError = string.Empty;
    private float? fileMoveProgress;
    private bool canAnswerFileMovePrompt;

    // Internal move variables, not exposed to the GUI
    private List<string>? fileMoveOfferFiles;
    private string? fileMoveOfferTarget;
    private Action? fileMoveFinishCallback;

    // Devcenter features
    private bool showDevCenterStatusArea = true;
    private bool showDevCenterPopup;

    private int nextDevCenterOpenOverrideKeyIndex;

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

        ApplySettings();
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

        Items = new ObservableCollection<string>();
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

    public bool HasDevCenterConnection => DevCenterConnection != null;

    public string DevCenterConnectedUser => DevCenterConnection?.Username ?? "error";

    public bool DevCenterConnectionIsDeveloper => DevCenterConnection?.IsDeveloper ?? false;

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

    public IEnumerable<FolderInInstallFolder>? InstalledFolders
    {
        get => installedFolders;
        set => this.RaiseAndSetIfChanged(ref installedFolders, value);
    }

    public string? SelectedVersionToPlay
    {
        get => selectedVersionToPlay;
        set => this.RaiseAndSetIfChanged(ref selectedVersionToPlay, value);
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

    public bool ShowSettingsPopup
    {
        get => showSettingsPopup;
        private set
        {
            if (showSettingsPopup == value)
                return;

            this.RaiseAndSetIfChanged(ref showSettingsPopup, value);

            if (!showSettingsPopup)
            {
                TriggerSaveSettings();
            }
            else
            {
                StartSettingsViewTasks();
            }
        }
    }

    public bool ShowDevCenterPopup
    {
        get => showDevCenterPopup;
        private set
        {
            if (showDevCenterPopup == value)
                return;

            this.RaiseAndSetIfChanged(ref showDevCenterPopup, value);

            if (!showDevCenterPopup)
            {
                // TODO: save settings if devbuild type or latest build was changed
                // TriggerSaveSettings();
            }
        }
    }

    public bool ShowDevCenterStatusArea
    {
        get => showDevCenterStatusArea;
        private set => this.RaiseAndSetIfChanged(ref showDevCenterStatusArea, value);
    }

    public bool HasPendingFileMoveOffer
    {
        get => hasPendingFileMoveOffer;
        private set
        {
            this.RaiseAndSetIfChanged(ref hasPendingFileMoveOffer, value);
        }
    }

    public bool CanAnswerFileMovePrompt
    {
        get => canAnswerFileMovePrompt;
        private set
        {
            this.RaiseAndSetIfChanged(ref canAnswerFileMovePrompt, value);
        }
    }

    public string FileMoveOfferTitle
    {
        get => fileMoveOfferTitle;
        private set
        {
            this.RaiseAndSetIfChanged(ref fileMoveOfferTitle, value);
        }
    }

    public string FileMoveOfferContent
    {
        get => fileMoveOfferContent;
        private set
        {
            this.RaiseAndSetIfChanged(ref fileMoveOfferContent, value);
        }
    }

    public string FileMoveOfferError
    {
        get => fileMoveOfferError;
        private set
        {
            this.RaiseAndSetIfChanged(ref fileMoveOfferError, value);
        }
    }

    public float? FileMoveProgress
    {
        get => fileMoveProgress;
        private set
        {
            this.RaiseAndSetIfChanged(ref fileMoveProgress, value);
        }
    }

    public Task<string> DehydrateCacheSize => dehydrateCacheSizeTask ?? throw new Exception("Constructor not ran");

    public DevCenterConnection? DevCenterConnection => devCenterClient.DevCenterConnection;

    public ObservableCollection<string> Items { get; }

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

    public void SetLoadCachedVersionInfo()
    {
        LauncherInfoLoadError = string.Empty;

        LoadCachedVersionInfo = true;
    }

    /// <summary>
    ///   Sorts versions to be in the order they should be shown to the user
    /// </summary>
    /// <param name="versions">The versions to sort</param>
    /// <returns>Versions in sorted order</returns>
    public IOrderedEnumerable<(string VersionName, IPlayableVersion VersionObject)> SortVersions(
        IEnumerable<(string VersionName, IPlayableVersion VersionObject)> versions)
    {
        // Store version first
        var sorted = versions.OrderBy(t => t.VersionObject is StoreVersion);

        // Then devbuilds (sorted by type)
        sorted = sorted.ThenBy(t =>
        {
            if (t.VersionObject is DevBuildVersion buildVersion)
            {
                return (int)buildVersion.BuildType;
            }

            return int.MaxValue;
        });

        var fallbackVersion = new Version(0, 0, 0, 0);
        var highestVersion = new Version(int.MaxValue, 0, 0, 0);

        sorted = sorted.ThenBy(t =>
        {
            if (t.VersionObject is StoreVersion or DevBuildVersion)
                return highestVersion;

            try
            {
                return new Version(t.VersionName.Split("-", 2)[0]);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Cannot parse version number for sorting: {VersionName}", t.VersionName);
                return fallbackVersion;
            }
        });

        return sorted;
    }

    public void CloseNotice()
    {
        NoticeMessageText = string.Empty;
        NoticeMessageTitle = string.Empty;
    }

    public void OpenSettings()
    {
        ShowSettingsPopup = !ShowSettingsPopup;
    }

    public void CloseSettingsClicked()
    {
        ShowSettingsPopup = !ShowSettingsPopup;
    }

    public void OpenDevCenterConnectionMenu()
    {
        ShowDevCenterPopup = true;
    }

    public void CloseDevCenterMenuClicked()
    {
        ShowDevCenterPopup = false;
    }

    public void DevCenterViewActivation(int keyIndex)
    {
        if (nextDevCenterOpenOverrideKeyIndex == keyIndex)
        {
            ++nextDevCenterOpenOverrideKeyIndex;

            if (nextDevCenterOpenOverrideKeyIndex >= TotalKeysInDevCenterActivationSequence)
            {
                OpenDevCenterConnectionMenu();
                nextDevCenterOpenOverrideKeyIndex = 0;
            }
        }
        else
        {
            // User failed to type the sequence
            nextDevCenterOpenOverrideKeyIndex = 0;
        }
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

    public void OpenLogsFolder()
    {
        FileUtilities.OpenFolderInPlatformSpecificViewer(launcherPaths.PathToLogFolder);
    }

    public void OpenFileBrowserToInstalled()
    {
        var folder = ThriveInstallationPath;

        if (!Directory.Exists(folder))
        {
            ShowNotice(Resources.FolderNotFound, Resources.ThriveInstallFolderNotFound);
            return;
        }

        FileUtilities.OpenFolderInPlatformSpecificViewer(folder);
    }

    public void ResetInstallLocation()
    {
        SetInstallPathTo(launcherPaths.PathToDefaultThriveInstallFolder);
    }

    public void SetInstallPathTo(string folder)
    {
        folder = folder.Replace('\\', '/');

        if (Settings.ThriveInstallationPath == folder)
            return;

        logger.LogInformation("Setting Thrive install path to {Folder}", folder);

        var rawFiles = thriveInstaller.DetectInstalledThriveFolders();

        var installedVersions = rawFiles.ToList();

        this.RaisePropertyChanging(nameof(ThriveInstallationPath));

        // If it is the default path, then we want to actually clear the option to null
        if (launcherPaths.PathToDefaultThriveInstallFolder == folder)
        {
            logger.LogInformation("Resetting install path to default value");
            Settings.ThriveInstallationPath = null;
        }
        else
        {
            logger.LogInformation("Setting install path to {Folder}", folder);
            Settings.ThriveInstallationPath = folder;
        }

        void OnFinished()
        {
            Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(ThriveInstallationPath)));

            TriggerSaveSettings();

            // Refresh the list of installed versions
            StartSettingsViewTasks();
        }

        // Offer to move over the already installed versions
        if (installedVersions.Count > 0)
        {
            OfferFileMove(installedVersions, ThriveInstallationPath, Resources.MoveVersionsTitle,
                Resources.MoveVersionsExplanation, OnFinished);
        }
        else
        {
            OnFinished();
        }
    }

    public void PerformFileMove()
    {
        CanAnswerFileMovePrompt = false;

        var task = new Task(() =>
        {
            if (fileMoveOfferFiles == null || string.IsNullOrEmpty(fileMoveOfferTarget))
            {
                logger.LogError("Files to move has not been set correctly");

                Dispatcher.UIThread.Post(() =>
                    ShowNotice(Resources.InternalErrorTitle, Resources.InternalErrorExplanation));
                return;
            }

            logger.LogDebug("Performing file move after offer accepted");

            var total = fileMoveOfferFiles.Count;
            var processed = 0;

            foreach (var file in fileMoveOfferFiles)
            {
                ++processed;

                // Skip if doesn't exist (to allow multiple tries to succeed if one attempt failed
                if (!File.Exists(file) && !Directory.Exists(file))
                    continue;

                try
                {
                    MoveFile(file, fileMoveOfferTarget);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to move files");

                    Dispatcher.UIThread.Post(() =>
                    {
                        FileMoveOfferError = e.Message;
                        CanAnswerFileMovePrompt = true;
                    });

                    return;
                }

                var progress = (float)processed / total;
                Dispatcher.UIThread.Post(() => FileMoveProgress = progress);
            }

            Dispatcher.UIThread.Post(() =>
            {
                HasPendingFileMoveOffer = false;
                fileMoveFinishCallback?.Invoke();
                fileMoveFinishCallback = null;
            });
        });

        task.Start();
    }

    public void CancelFileMove()
    {
        CanAnswerFileMovePrompt = false;
        HasPendingFileMoveOffer = false;

        fileMoveFinishCallback?.Invoke();
        fileMoveFinishCallback = null;
    }

    public bool DeleteVersion(string versionFolderName)
    {
        var target = Path.Join(ThriveInstallationPath, versionFolderName);

        if (!Directory.Exists(target))
        {
            logger.LogError("Can't delete non-existent folder: {Target}", target);

            ShowNotice(Resources.DeleteErrorTitle, Resources.DeleteErrorDoesNotExist);
            return false;
        }

        logger.LogInformation("Deleting version: {Target}", target);

        try
        {
            Directory.Delete(target, true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete {Target}", target);
            ShowNotice(Resources.DeleteErrorTitle, string.Format(Resources.DeleteErrorExplanation, target, e.Message));
            return false;
        }

        // Refresh the version list if it might be visible
        if (ShowSettingsPopup)
        {
            StartSettingsViewTasks();
        }

        return true;
    }

    private void OfferFileMove(List<string> filesToMove, string newFolder, string popupTitle, string popupText,
        Action onFinished)
    {
        if (HasPendingFileMoveOffer)
            throw new InvalidOperationException("File move offer already pending");

        HasPendingFileMoveOffer = true;
        CanAnswerFileMovePrompt = true;
        FileMoveOfferError = string.Empty;
        FileMoveProgress = null;

        fileMoveOfferFiles = filesToMove;
        fileMoveOfferTarget = newFolder;

        FileMoveOfferTitle = popupTitle;
        FileMoveOfferContent = popupText;

        fileMoveFinishCallback = onFinished;
    }

    private void MoveFile(string file, string targetFolder, bool overwrite = false)
    {
        Directory.CreateDirectory(targetFolder);

        var target = Path.Join(targetFolder, Path.GetFileName(file));

        logger.LogInformation("Moving {File} -> {Target}", file, target);

        bool isFolder = Directory.Exists(file);

        if (isFolder)
        {
            logger.LogDebug("Moved file is a folder");
        }
        else
        {
            logger.LogDebug("Moved file is a file");
        }

        bool tryCopyAndMove = false;

        try
        {
            if (isFolder)
            {
                if (overwrite && Directory.Exists(target))
                {
                    logger.LogInformation("Deleting existing folder to move over it: {Target}", target);
                    Directory.Delete(target);
                }

                Directory.Move(file, target);
            }
            else
            {
                File.Move(file, target, overwrite);
            }
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "Can't move using move, trying copy and delete instead");
            tryCopyAndMove = true;
        }

        if (tryCopyAndMove)
        {
            if (isFolder)
            {
                Directory.CreateDirectory(target);
                CopyHelpers.CopyFoldersRecursivelyWithSymlinks(file, target, overwrite);
                Directory.Delete(file);
            }
            else
            {
                File.Copy(file, target, overwrite);
                File.Delete(file);
            }
        }
    }

    private void StartSettingsViewTasks()
    {
        if (DehydrateCacheSize.Status == TaskStatus.Created)
            DehydrateCacheSize.Start();

        Task.Run(() =>
        {
            var installed = thriveInstaller.ListFoldersInThriveInstallFolder().ToList();

            Dispatcher.UIThread.Post(() => InstalledFolders = installed);
        });
    }

    private void TriggerSaveSettings()
    {
        Dispatcher.UIThread.Post(PerformSave);
    }

    private async void PerformSave()
    {
        // TODO: only save if there are settings changes

        if (!await settingsManager.Save())
        {
            ShowNotice(Resources.SettingsSaveFailedTitle, Resources.SettingsSaveFailedMessage);
        }
        else
        {
            logger.LogInformation("Settings have been saved");
        }
    }

    private void CreateSettingsTabTasks()
    {
        dehydrateCacheSizeTask ??= new Task<string>(() => ComputeDehydrateCacheSizeDisplayString().Result);
    }

    private void RefreshDehydratedCacheSize()
    {
        dehydrateCacheSizeTask = null;
        CreateSettingsTabTasks();

        StartSettingsViewTasks();

        this.RaisePropertyChanged(nameof(DehydrateCacheSize));
    }

    private async Task<string> ComputeDehydrateCacheSizeDisplayString()
    {
        var calculateTask = new Task<long>(() =>
            FileUtilities.CalculateFolderSize(DehydratedCacheFolder));
        calculateTask.Start();

        await calculateTask.WaitAsync(CancellationToken.None);
        var size = calculateTask.Result;

        return string.Format(Resources.SizeInMiB, Math.Round((float)size / GlobalConstants.MEBIBYTE, 1));
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

                    // TODO: detect current launcher version being outdated and trigger auto-update
                    // TODO: add option to disable auto-update
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
                    var result = await launcherInfoRetriever.LoadFromCache();

                    if (result == null)
                        throw new Exception("Loading cached file failed");

                    return result;
                }

                logger.LogInformation("Fetching Thrive launcher info");
                RetryVersionInfoDownload = false;
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

    private void OnDevCenterConnectionStatusChanged()
    {
        this.RaisePropertyChanged(nameof(DevCenterConnection));
        this.RaisePropertyChanged(nameof(HasDevCenterConnection));
        this.RaisePropertyChanged(nameof(DevCenterConnectedUser));
        this.RaisePropertyChanged(nameof(DevCenterConnectionIsDeveloper));
        this.RaisePropertyChanged(nameof(AvailableThriveVersions));
    }
}
