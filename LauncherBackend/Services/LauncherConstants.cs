namespace LauncherBackend.Services;

using System.Text.RegularExpressions;
using DevCenterCommunication.Utilities;
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
    public const LauncherMode Mode = LauncherMode.Production;

    /// <summary>
    ///   File name to determine which exact version is extracted as a DevBuild
    /// </summary>
    public const string DevBuildCacheFileName = "devbuild_cache.json";

    public const string DehydratedCacheFileName = DehydrateCache.CacheFileName;

    public const string DevBuildFileName = "devbuild.7z";

    public const string ToolsFolderName = "tools";
    public const string TemporaryExtractedFolderName = "temporary_extracted";

    public const int SimultaneousRehydrationDownloads = 3;

    public const int DownloadBufferSize = 65536;

    public const bool EntirelyHideWindowOnHide = false;

    public const int ThriveStartupFailureRetries = 1;

    /// <summary>
    ///   This is used to globally (computer-wide) mark that the launcher is open
    /// </summary>
    public const string LauncherGlobalMemoryMapName = "ThriveLauncher-b27a18de-8f7b-48b9-b54c-75ab3a3816e9";

    /// <summary>
    ///   Maximum size in bytes for a file to be included in crash report
    /// </summary>
    public const long MaxCrashLogFileSize = 2 * GlobalConstants.MEBIBYTE;

    public const int FeedExcerptLength = 450;

    public const int DefaultFirstLinesToKeepOfThriveOutput = 100;
    public const int DefaultLastLinesToKeepOfThriveOutput = 900;

    public const bool UsePlatformLineSeparatorsInCopiedLog = true;

    // TODO: combine to a common module with Thrive as these are there as well
    public const string OPENED_THROUGH_LAUNCHER_OPTION = "--thrive-started-by-launcher";
    public const string OPENING_LAUNCHER_IS_HIDDEN = "--thrive-launcher-hidden";
    public const string THRIVE_LAUNCHER_STORE_PREFIX = "--thrive-store=";
    public const string STARTUP_SUCCEEDED_MESSAGE = "------------ Thrive Startup Succeeded ------------";
    public const string USER_REQUESTED_QUIT = "User requested program exit, Thrive will close shortly";
    public const string REQUEST_LAUNCHER_OPEN = "------------ SHOWING LAUNCHER REQUESTED ------------";

    public const string DefaultThriveLogFileName = "log.txt";

    public const string ThriveCrashesFolderName = "crashes";
    public const string ThriveLogsFolderName = "logs";

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

    public const string LauncherLinkingInstructionsURL =
        "https://wiki.revolutionarygamesstudio.com/wiki/Linking_the_Launcher";

    public const string StartOfUnhandledExceptionLogging = "------------ Begin of Unhandled Exception";
    public const string EndOfUnhandledExceptionLogging = "------------  End of Unhandled Exception";

    public static readonly string ModeSuffix = Mode switch
    {
        // ReSharper disable HeuristicUnreachableCode
        LauncherMode.Staging => "-staging",
        LauncherMode.LocalTesting => "-test",

        // ReSharper restore HeuristicUnreachableCode
        _ => string.Empty,
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

#pragma warning disable CS0162

    // ReSharper disable HeuristicUnreachableCode
    // This goes through a CDN in production
    public static readonly Uri LauncherInfoFileURL =
        Mode == LauncherMode.Production ?
            new Uri("https://thrivelauncher-info.b-cdn.net/") :
            new Uri(DevCenterURL, "api/v1/LauncherInfo");

    // ReSharper restore HeuristicUnreachableCode
#pragma warning restore CS0162

    public static readonly Uri DevCenterUserTokenURL = new(DevCenterURL, "me");

    public static readonly Uri DevCenterBuildInfoPagePrefix = new(DevCenterURL, "builds/");

    public static readonly Uri DevCenterCrashReportURL = new(DevCenterURL, "api/v1/crashReport");

    public static readonly Uri DevCenterCrashReportInfoPrefix = new(DevCenterURL, "reports/");
    public static readonly Uri DevCenterCrashReportDeletePrefix = new(DevCenterURL, "deleteReport/");

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

    /// <summary>
    ///   Time to wait before closing the launcher after auto launch to detect if the game immediately crashed.
    /// </summary>
    public static readonly TimeSpan CloseDelayAfterAutoStart = TimeSpan.FromMilliseconds(350);

    /// <summary>
    ///   How long Thrive must have ran to not show the game start failure advice
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     This is now much longer as the launcher can detect proper Thrive startup now
    ///   </para>
    /// </remarks>
    public static readonly TimeSpan RequiredRuntimeBeforeGameStartAdviceDisappears = TimeSpan.FromSeconds(60);

    /// <summary>
    ///   Time before hiding the launcher when starting the game
    /// </summary>
    public static readonly TimeSpan MinimizeDelayAfterGameStart = TimeSpan.FromMilliseconds(800);

    public static readonly TimeSpan RestoreDelayAfterGameEnd = TimeSpan.FromMilliseconds(500);

    public static readonly TimeSpan DelayForBackendStateCheckOnStart = TimeSpan.FromMilliseconds(500);

    public static readonly TimeSpan OldCrashReportWarningThreshold = TimeSpan.FromDays(1);

    public static readonly TimeSpan LauncherNotUpdatingWarningThreshold = TimeSpan.FromDays(7);

    public static readonly DateTime DevBuildNewerThanThisSupportStartupDetection =
        new(2022, 11, 23, 12, 0, 0, DateTimeKind.Utc);

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
