namespace ThriveLauncher.Models;

using CommandLine;

public class Options
{
    public Options(bool? verbose, string? language)
    {
        Verbose = verbose;
        Language = language;
    }

    /// <summary>
    ///   Default options to be used at design time, or when nothing gets configured through the command line
    /// </summary>
    public Options()
    {
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
}
