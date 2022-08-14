using System.Collections.ObjectModel;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ThriveLauncher.Utilities;

namespace ThriveLauncher.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private const int TotalKeysInDevCenterActivationSequence = 4;

        private readonly ILauncherFeeds launcherFeeds;
        private readonly IStoreVersionDetector storeInfo;
        private readonly VersionUtilities versionUtilities;

        private readonly bool isStoreVersion;

        private string noticeMessageText = string.Empty;
        private string noticeMessageTitle = string.Empty;
        private bool canDismissNotice = true;

        private bool showLinksPopup;

        private bool showSettingsPopup;
        private bool webFeedsEnabled;

        private bool showDevCenterStatusArea = true;
        private bool showDevCenterPopup;

        private DevCenterConnection? devCenterConnection;
        private string devCenterUsername = string.Empty;

        private bool preventDevCenterFeatures;
        private int nextDevCenterOpenOverrideKeyIndex;

        public MainWindowViewModel(ILauncherFeeds launcherFeeds, IStoreVersionDetector storeInfo,
            ILauncherSettingsManager settingsManager, VersionUtilities versionUtilities)
        {
            this.launcherFeeds = launcherFeeds;
            this.storeInfo = storeInfo;
            this.versionUtilities = versionUtilities;

            ApplySettings(settingsManager.Settings);

            var detectedStore = storeInfo.Detect();
            isStoreVersion = detectedStore.IsStoreVersion;
            preventDevCenterFeatures = detectedStore.ShouldPreventDefaultDevCenterVisibility;

            // TODO: devcenter visibility when already configured or through a specific secret key sequence?

            if (preventDevCenterFeatures)
                ShowDevCenterStatusArea = false;

            Items = new ObservableCollection<string>() { };
        }

        /// <summary>
        ///   Constructor for live preview
        /// </summary>
        public MainWindowViewModel() : this(DesignTimeServices.Services.GetRequiredService<ILauncherFeeds>(),
            DesignTimeServices.Services.GetRequiredService<IStoreVersionDetector>(),
            DesignTimeServices.Services.GetRequiredService<ILauncherSettingsManager>(),
            DesignTimeServices.Services.GetRequiredService<VersionUtilities>())
        {
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

        public bool ShowLinksPopup
        {
            get => showLinksPopup;
            private set
            {
                this.RaiseAndSetIfChanged(ref showLinksPopup, value);
            }
        }

        public bool ShowSettingsPopup
        {
            get => showSettingsPopup;
            private set
            {
                this.RaiseAndSetIfChanged(ref showSettingsPopup, value);
            }
        }

        public bool ShowDevCenterPopup
        {
            get => showDevCenterPopup;
            private set
            {
                this.RaiseAndSetIfChanged(ref showDevCenterPopup, value);
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

        public bool WebFeedsEnabled
        {
            get => webFeedsEnabled;
            private set
            {
                this.RaiseAndSetIfChanged(ref webFeedsEnabled, value);
            }
        }

        public ObservableCollection<string> Items { get; }

        public void ApplySettings(LauncherSettings settings)
        {
            WebFeedsEnabled = settings.ShowWebContent;
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
    }
}
