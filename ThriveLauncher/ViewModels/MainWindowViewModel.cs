using System.Collections.ObjectModel;
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

        public bool HasNoticeMessage =>
            !string.IsNullOrEmpty(NoticeMessageText) || !string.IsNullOrEmpty(NoticeMessageTitle);

        public bool CanCloseNotice => true;

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
                this.RaisePropertyChanged(nameof(CanCloseNotice));
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
                this.RaisePropertyChanged(nameof(CanCloseNotice));
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
