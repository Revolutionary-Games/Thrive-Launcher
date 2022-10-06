namespace ThriveLauncher.Models;

using System.Collections.Generic;
using CommandLine;

public class Options
{
    public Options(bool? verbose, string? language, IList<string> thriveExtraFlags)
    {
        Verbose = verbose;
        Language = language;
        ThriveExtraFlags = thriveExtraFlags;
    }

    /// <summary>
    ///   Default options to be used at design time, or when nothing gets configured through the command line
    /// </summary>
    public Options()
    {
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

    // TODO: forwarding linux preload environment variables

    // TODO: seamless launcher mode settings

    // TODO: option to disable auto starting Thrive

    // TODO: handle this
    [Value(0, MetaName = "THRIVE_OPTIONS", HelpText = "Extra flags to pass to Thrive processes when starting them",
        Required = false)]
    public IList<string> ThriveExtraFlags { get; }
}
