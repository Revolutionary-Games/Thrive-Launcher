namespace LauncherBackend.Services;

using System.Text.RegularExpressions;
using SharedBase.Utilities;

/// <summary>
///   Holds launcher global configuration constants
/// </summary>
public static class LauncherConstants
{
    /// <summary>
    ///   The mode the launcher is in. ONLY COMMIT WHEN IN PRODUCTION MODE. This can be changed temporarily for local
    ///   testing.
    /// </summary>
    public const LauncherMode Mode = LauncherMode.LocalTesting;

    public const string DevBuildCacheFileName = "devbuild_cache.json";

    /// <summary>
    ///   Maximum size in bytes for a file to be included in crash report
    /// </summary>
    public const long MaxCrashLogFileSize = 2 * GlobalConstants.MEBIBYTE;

    public const int FeedExcerptLength = 450;

    // URLs to our resources
    public const string MainSiteURL = "https://revolutionarygamesstudio.com";
    public const string DevelopmentForumsURL = "https://forum.revolutionarygamesstudio.com/";
    public const string CommunityForumsURL = "https://community.revolutionarygamesstudio.com/";
    public const string ThriveRepoURL = "https://github.com/Revolutionary-Games/Thrive";
    public const string ThrivePatreonURL = "https://www.patreon.com/thrivegame";
    public const string DonateURL = "https://revolutionarygamesstudio.com/donate/";
    public const string ThriveSteamURL = "https://store.steampowered.com/app/1779200";
    public const string ThriveItchURL = "https://revolutionarygames.itch.io/thrive";
    public const string LauncherRepoURL = "https://github.com/Revolutionary-Games/Thrive-Launcher";
    public const string DeveloperWikiURL = "https://wiki.revolutionarygamesstudio.com/";
    public const string ThriveDevCenterBrowserURL = "https://dev.revolutionarygamesstudio.com/";
    public const string FanWikiURL = "https://thrive.wikia.com/wiki/Thrive_Wiki";
    public const string DiscordServerURL = "https://discord.gg/FZxDQ4H";
    public const string SubredditURL = "https://www.reddit.com/r/thrive";
    public const string YoutubeChannelURL = "https://www.youtube.com/c/RevolutionaryGames";
    public const string FacebookPageURL = "https://www.facebook.com/Thrive-182887991751358/";
    public const string TwitterProfileURL = "https://twitter.com/thrive_game";
    public const string LauncherDownloadsPageURL = "https://github.com/Revolutionary-Games/Thrive-Launcher/releases";
    public const string LauncherLinkingInstructionsURL = "https://wiki.revolutionarygamesstudio.com/wiki/Linking_the_Launcher";

    public static readonly string ModeSuffix = Mode switch
    {
        // ReSharper disable HeuristicUnreachableCode
        LauncherMode.Staging => "-staging",
        LauncherMode.LocalTesting => "-test",

        // ReSharper restore HeuristicUnreachableCode
        _ => "",
    };

    public static readonly Uri DevCenterURL = new(Mode switch
    {
        // ReSharper disable HeuristicUnreachableCode
        LauncherMode.Staging => "https://staging.dev.revolutionarygamesstudio.com/",
        LauncherMode.LocalTesting => "http://localhost:5000",

        // ReSharper restore HeuristicUnreachableCode
        _ => "https://dev.revolutionarygamesstudio.com/",
    });

    public static readonly List<string> SigningManifestResourceNames = new(Mode switch
    {
        // ReSharper disable HeuristicUnreachableCode
        LauncherMode.Staging => new List<string> { "staging_1.cert", "staging_2.cert" },
        LauncherMode.LocalTesting => new List<string>(),

        // ReSharper restore HeuristicUnreachableCode
        _ => new List<string> { "production_1.cert", "production_2.cert" },
    });

    public static readonly Uri DevForumFeedURL = new("https://thrivefeeds.b-cdn.net/posts.rss");
    public static readonly Uri MainSiteFeedURL = new("https://thrivefeeds.b-cdn.net/feed.rss");

    public static readonly Uri LauncherInfoFileURL = new(DevCenterURL, "api/v1/LauncherInfo");

    public static readonly Uri DevCenterUserTokenURL = new(DevCenterURL, "me");

    /// <summary>
    ///   Regex used to detect current log file in game output
    /// </summary>
    public static readonly Regex ThriveOutputLogLocation = new(@"logs are written to:\s+(\S+).+log.+'(\S+)'",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    ///   Regex used to detect crash dumps in game output
    /// </summary>
    public static readonly Regex CrashDumpRegex = new(@"Crash dump created at:\s+(\S+\.dmp)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static readonly Regex YoutubeURLRegex = new(@"^http.*youtube.com\/.*embed\/(\w+)\?.*",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // TODO: may of the following TimeSpans won't matter in the launcher 2.0 version

    /// <summary>
    ///   Time to wait before handling an error signal to allow time for exit signal, which is much more useful for
    ///   subprocess handling.
    /// </summary>
    public static readonly TimeSpan MaxDelayBetweenExitAfterErrorSignal = TimeSpan.FromMilliseconds(150);

    /// <summary>
    ///   Time to wait before closing the launcher after auto launch to detect if the game immediately crashed.
    /// </summary>
    public static readonly TimeSpan CloseDelayAfterAutoStart = TimeSpan.FromMilliseconds(350);

    /// <summary>
    ///   Time that the game must have run to auto close without error
    /// </summary>
    public static readonly TimeSpan AutoCloseMinimumGameDuration = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    ///   Time before hiding the launcher when starting the game
    /// </summary>
    public static readonly TimeSpan MinimizeDelayAfterGameStart = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///   Time to check that the game process has properly launched, and hasn't suddenly died
    /// </summary>
    public static readonly TimeSpan CheckLauncherProcessIsRunningDelay = TimeSpan.FromMilliseconds(750);

    // Time in milliseconds to wait once the game has exited for last log messages to arrive
    // before doing post game actions. Doesn't seem to actually help if the game immediately
    // crashed...
    public static readonly TimeSpan WaitLogsAfterGameClose = TimeSpan.FromMilliseconds(100);

    // TODO: implement a script to fetch test version data and a variable to configure loading it

    /// <summary>
    ///   Configures the launcher URL connection modes
    /// </summary>
    public enum LauncherMode
    {
        Production,
        Staging,
        LocalTesting,
    }
}
