using System.Collections.ObjectModel;
using Avalonia.Metadata;
using LauncherBackend.Models;
using ReactiveUI;

namespace ThriveLauncher.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly bool isStoreVersion;

        private string noticeMessageText = string.Empty;
        private string noticeMessageTitle = string.Empty;
        private bool showLinksPopup;
        private bool showDevCenterStatusArea = true;
        private DevCenterConnection? devCenterConnection;
        private bool webFeedsEnabled;

        private string devCenterUsername = string.Empty;

        private bool preventDevCenterFeatures;

        public MainWindowViewModel()
        {
            isStoreVersion = StoreVersionInfo.Instance.IsStoreVersion;
            preventDevCenterFeatures = StoreVersionInfo.Instance.ShouldPreventDefaultDevCenterVisibility;

            // TODO: devcenter visibility when already configured or through a specific secret key sequence?

            if (preventDevCenterFeatures)
                ShowDevCenterStatusArea = false;

            Items = new ObservableCollection<string>() { };
        }

        [DependsOn(nameof(NoticeMessageText))]
        [DependsOn(nameof(NoticeMessageTitle))]
        public bool HasNoticeMessage =>
            !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

        [DependsOn(nameof(DevCenterConnection))]
        public bool HasDevCenterConnection => DevCenterConnection != null;

        [DependsOn(nameof(DevCenterConnection))]
        public string DevCenterConnectedUser => DevCenterConnection?.Username ?? "error";

        [DependsOn(nameof(DevCenterConnection))]
        public bool DevCenterConnectionIsDeveloper => DevCenterConnection?.IsDeveloper ?? false;

        public string NoticeMessageText
        {
            get => noticeMessageText;
            private set
            {
                this.RaiseAndSetIfChanged(ref noticeMessageText, value);
            }
        }

        public string NoticeMessageTitle
        {
            get => noticeMessageTitle;
            private set
            {
                this.RaiseAndSetIfChanged(ref noticeMessageTitle, value);
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

        public void CloseNotice()
        {
            NoticeMessageText = string.Empty;
            NoticeMessageTitle = string.Empty;
        }

        public void LinksButtonClicked()
        {
            ShowLinksPopup = !ShowLinksPopup;
        }

        public void CloseLinksClicked()
        {
            ShowLinksPopup = false;
        }

        public void VersionSelected(string selectedVersion)
        {
        }

        public void OpenDevCenterConnectionMenu()
        {
        }
    }
}
