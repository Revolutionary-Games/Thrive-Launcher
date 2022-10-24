namespace ThriveLauncher.Models;

using System.Collections.Generic;
using CommandLine;
using LauncherBackend.Models;

public class Options : ILauncherOptions
{
    public Options(bool? verbose, string? language, bool skipAutoUpdate,
        bool disableSeamlessMode, bool allowSeamlessMode, string? gameLDPreload, IList<string> thriveExtraFlags,
        bool dummyOpenDev, bool dummyNoSandbox, string? dummyRemoteDebuggingPort)
    {
        Verbose = verbose;
        Language = language;
        SkipAutoUpdate = skipAutoUpdate;
        DisableSeamlessMode = disableSeamlessMode;
        AllowSeamlessMode = allowSeamlessMode;
        GameLDPreload = gameLDPreload;

        ThriveExtraFlags = thriveExtraFlags;

        DummyOpenDev = dummyOpenDev;
        DummyNoSandbox = dummyNoSandbox;
        DummyRemoteDebuggingPort = dummyRemoteDebuggingPort;
    }

    /// <summary>
    ///   Default options to be used at design time, or when nothing gets configured through the command line
    /// </summary>
    public Options()
    {
        SkipAutoUpdate = true;
        DisableSeamlessMode = true;
        AllowSeamlessMode = false;
        ThriveExtraFlags = new List<string>();
    }

#if DEBUG
    [Option('v', "verbose", Default = true, HelpText = "Set output to verbose messages")]
    public bool? Verbose { get; } = true;
#else
    [Option('v', "verbose", HelpText = "Set output to verbose messages")]
    public bool? Verbose { get; }
#endif

    [Option('l', "language", Default = null,
        HelpText = "Override launcher language (use en-GB for maximum stability)")]
    public string? Language { get; }

    [Option("skip-autoupdate", Default = false,
        HelpText = "Skip checking and applying auto updates to the launcher")]
    public bool SkipAutoUpdate { get; }

    [Option("no-autorun", Default = false,
        HelpText = "Skip allowing the game to be automatically started")]
    public bool DisableSeamlessMode { get; }

    [Option("allow-seamless-mode", Default = false,
        HelpText = "Used to detect when seamless mode is allowed")]
    public bool AllowSeamlessMode { get; }

    [Option("game-ld-preload", Default = null,
        HelpText = "Set what to pass as LD_PRELOAD to the game process when started")]
    public string? GameLDPreload { get; }

    // TODO: handle this
    [Value(0, MetaName = "THRIVE_OPTIONS", HelpText = "Extra flags to pass to Thrive processes when starting them",
        Required = false)]
    public IList<string> ThriveExtraFlags { get; }

    [Option("open-dev", HelpText = "DEPRECATED flag, kept for compatibility with Launcher 1.0")]
    public bool DummyOpenDev { get; }

    [Option("no-sandbox", HelpText = "DEPRECATED flag, kept for compatibility with Launcher 1.0")]
    public bool DummyNoSandbox { get; }

    [Option("remote-debugging-port", HelpText = "DEPRECATED flag, kept for compatibility with Launcher 1.0")]
    public string? DummyRemoteDebuggingPort { get; }
}
