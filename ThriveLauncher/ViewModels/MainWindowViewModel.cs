using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SharedBase.Utilities;
using ThriveLauncher.Properties;
using ThriveLauncher.Utilities;

namespace ThriveLauncher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int TotalKeysInDevCenterActivationSequence = 4;

    private readonly ILogger<MainWindowViewModel> logger;
    private readonly ILauncherFeeds launcherFeeds;
    private readonly IStoreVersionDetector storeInfo;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly VersionUtilities versionUtilities;
    private readonly ILauncherPaths launcherPaths;

    private readonly Dictionary<string, CultureInfo> availableLanguages;
    private readonly StoreVersionInfo detectedStore;

    private string noticeMessageText = string.Empty;
    private string noticeMessageTitle = string.Empty;
    private bool canDismissNotice = true;

    private bool showSettingsUpgrade;

    // Feeds
    private Task<List<ParsedLauncherFeedItem>> devForumFeedItems = null!;
    private string? devForumFetchError;

    private Task<List<ParsedLauncherFeedItem>> mainSiteFeedItems = null!;
    private string? mainSiteFetchError;

    // Settings sub view
    private bool showSettingsPopup;

    private Task<string>? dehydrateCacheSizeTask;

    // Devcenter features
    private bool showDevCenterStatusArea = true;
    private bool showDevCenterPopup;

    private DevCenterConnection? devCenterConnection;

    private int nextDevCenterOpenOverrideKeyIndex;

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ILauncherFeeds launcherFeeds,
        IStoreVersionDetector storeInfo,
        ILauncherSettingsManager settingsManager, VersionUtilities versionUtilities, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.launcherFeeds = launcherFeeds;
        this.storeInfo = storeInfo;
        this.settingsManager = settingsManager;
        this.versionUtilities = versionUtilities;
        this.launcherPaths = launcherPaths;

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

        CreateFeedRetrieveTasks();

        if (Settings.ShowWebContent)
        {
            devForumFeedItems.Start();
            mainSiteFeedItems.Start();
        }

        items = new ObservableCollection<string>() { };
    }

    /// <summary>
    ///   Constructor for live preview
    /// </summary>
    public MainWindowViewModel() : this(DesignTimeServices.Services.GetRequiredService<ILogger<MainWindowViewModel>>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherFeeds>(),
        DesignTimeServices.Services.GetRequiredService<IStoreVersionDetector>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherSettingsManager>(),
        DesignTimeServices.Services.GetRequiredService<VersionUtilities>(),
        DesignTimeServices.Services.GetRequiredService<ILauncherPaths>())
    {
        languagePlaceHolderIfNotSelected = string.Empty;
        CreateFeedRetrieveTasks();
    }

    public bool HasNoticeMessage =>
        !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

    public string LauncherVersion => versionUtilities.LauncherVersion + LauncherConstants.ModeSuffix;

    public bool CanDismissNotice
    {
        get => HasNoticeMessage && canDismissNotice;
        private set
        {
            this.RaiseAndSetIfChanged(ref canDismissNotice, value);
        }
    }

    public bool HasDevCenterConnection => DevCenterConnection != null;

    public string DevCenterConnectedUser => DevCenterConnection?.Username ?? "error";

    public bool DevCenterConnectionIsDeveloper => DevCenterConnection?.IsDeveloper ?? false;

    public bool ShowLinksNotInSteamVersion => !detectedStore.IsSteam;

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

    public string? DevForumFetchError
    {
        get => devForumFetchError;
        private set
        {
            this.RaiseAndSetIfChanged(ref devForumFetchError, value);
        }
    }

    public Task<List<ParsedLauncherFeedItem>> DevForumFeedItems => devForumFeedItems;

    public string? MainSiteFetchError
    {
        get => mainSiteFetchError;
        private set
        {
            this.RaiseAndSetIfChanged(ref mainSiteFetchError, value);
        }
    }

    public Task<List<ParsedLauncherFeedItem>> MainSiteFeedItems => mainSiteFeedItems;

    public bool ShowSettingsUpgrade
    {
        get => showSettingsUpgrade;
        private set
        {
            this.RaiseAndSetIfChanged(ref showSettingsUpgrade, value);
        }
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
                TriggerSaveSettings();
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
        private set
        {
            this.RaiseAndSetIfChanged(ref showDevCenterStatusArea, value);
        }
    }

    public Task<string> DehydrateCacheSize => dehydrateCacheSizeTask ??= ComputeDehydrateCacheSizeDisplayString();

    public DevCenterConnection? DevCenterConnection
    {
        get => devCenterConnection;
        private set
        {
            if (devCenterConnection == value)
                return;

            this.RaiseAndSetIfChanged(ref devCenterConnection, value);
            this.RaisePropertyChanged(nameof(HasDevCenterConnection));
            this.RaisePropertyChanged(nameof(DevCenterConnectedUser));
            this.RaisePropertyChanged(nameof(DevCenterConnectionIsDeveloper));
        }
    }

    public ObservableCollection<string> items { get; }

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

    public void VersionSelected(string selectedVersion)
    {
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

        this.RaisePropertyChanging(nameof(ThriveInstallationPath));

        var previousPath = ThriveInstallationPath;

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

        // TODO: detect existing folders in the moved location and offer moving them
        throw new NotImplementedException();

        this.RaisePropertyChanged(nameof(ThriveInstallationPath));
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

    private async Task<string> ComputeDehydrateCacheSizeDisplayString()
    {
        var calculateTask = new Task<long>(() => FileUtilities.CalculateFolderSize(DehydratedCacheFolder));
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

    private async Task<List<ParsedLauncherFeedItem>> FetchFeed(string name, Uri uri, bool mainSite)
    {
        logger.LogDebug("Fetching feed {Name}", name);

        var (failure, data) = await launcherFeeds.FetchFeed(name, uri);

        if (failure != null)
        {
            SetFetchError(mainSite, failure);
            return new List<ParsedLauncherFeedItem>();
        }

        return data!;
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
}
