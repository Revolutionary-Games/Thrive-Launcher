namespace ThriveLauncher.ViewModels;

using LauncherBackend.Services;
using LauncherBackend.Utilities;
using ReactiveUI;

/// <summary>
///   The link buttons features
/// </summary>
public partial class MainWindowViewModel
{
    private bool showLinksPopup;

    public bool ShowLinksPopup
    {
        get => showLinksPopup;
        private set => this.RaiseAndSetIfChanged(ref showLinksPopup, value);
    }

    public void ToggleLinksView()
    {
        ShowLinksPopup = !ShowLinksPopup;
    }

    public void CloseLinksClicked()
    {
        ShowLinksPopup = false;
    }

    // Click handlers for the links
    public void OpenMainSiteLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.MainSiteURL);
    }

    public void OpenDevelopmentForumsLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DevelopmentForumsURL);
    }

    public void OpenCommunityForumsLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.CommunityForumsURL);
    }

    public void OpenThriveRepoLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.ThriveRepoURL);
    }

    public void OpenThrivePatreonLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.ThrivePatreonURL);
    }

    public void OpenDonateLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DonateURL);
    }

    public void OpenThriveSteamLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.ThriveSteamURL);
    }

    public void OpenThriveItchLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.ThriveItchURL);
    }

    public void OpenLauncherRepoLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.LauncherRepoURL);
    }

    public void OpenDeveloperWikiLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DeveloperWikiURL);
    }

    public void OpenThriveDevCenterBrowserLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.ThriveDevCenterBrowserURL);
    }

    public void OpenFanWikiLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.FanWikiURL);
    }

    public void OpenDiscordServerLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DiscordServerURL);
    }

    public void OpenSubredditLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.SubredditURL);
    }

    public void OpenYoutubeChannelLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.YoutubeChannelURL);
    }

    public void OpenFacebookPageLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.FacebookPageURL);
    }

    public void OpenTwitterProfileLink()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.TwitterProfileURL);
    }

    public void VisitDownloadsPage()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.LauncherDownloadsPageURL);
    }

    public void VisitDevCenterTokenManagement()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DevCenterUserTokenURL.ToString());
    }
}
