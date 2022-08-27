using System.Text.RegularExpressions;

namespace LauncherBackend.Services;

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

    public const string DevBuildCacheFileName = "devbuild_cache.json";

    /// <summary>
    ///   Maximum size in bytes for a file to be included in crash report
    /// </summary>
    public const long MaxCrashLogFileSize = 2000000;

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
        LocalTesting
    }
}
