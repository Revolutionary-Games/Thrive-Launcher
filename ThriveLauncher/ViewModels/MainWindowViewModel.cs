using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

    private bool showLinksPopup;

    private bool showSettingsUpgrade;

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

        if (Settings.ShowWebContent)
        {
            // TODO: start fetching web content here or should we use another approach?
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
    }

    public bool HasNoticeMessage =>
        !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

    public string LauncherVersion => versionUtilities.LauncherVersion;

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

    public bool ShowLinksPopup
    {
        get => showLinksPopup;
        private set
        {
            this.RaiseAndSetIfChanged(ref showLinksPopup, value);
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

    public void ToggleLinksView()
    {
        ShowLinksPopup = !ShowLinksPopup;
    }

    public void CloseLinksClicked()
    {
        ShowLinksPopup = false;
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
}
