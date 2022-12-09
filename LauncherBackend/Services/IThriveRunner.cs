namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using Models;

/// <summary>
///   Handles starting Thrive and getting the output from it (also handles triggering the Thrive crash reporter)
/// </summary>
public interface IThriveRunner
{
    /// <summary>
    ///   General messages about the playing process
    /// </summary>
    public ObservableCollection<ThrivePlayMessage> PlayMessages { get; }

    /// <summary>
    ///   Game output messages from Thrive that are kept in memory (this is limited to not consume a ton of memory,
    ///   full game output is available in the game logs). This is the first 100 or so lines with some further lines
    ///   being in the <see cref="ThriveOutputTrailing"/> property.
    /// </summary>
    public ObservableCollection<ThriveOutputMessage> ThriveOutput { get; }

    public ObservableCollection<ThriveOutputMessage> ThriveOutputTrailing { get; }

    public bool ThriveRunning { get; }

    /// <summary>
    ///   True when there's so much game output that it wasn't kept entirely in <see cref="ThriveOutput"/> and
    ///   <see cref="ThriveOutputTrailing"/>
    /// </summary>
    public bool OutputTruncated { get; }

    public IObservable<bool> OutputTruncatedObservable { get; }

    /// <summary>
    ///   Allows subscribing to <see cref="ThriveRunning"/> state changes
    /// </summary>
    public IObservable<bool> ThriveRunningObservable { get; }

    public string? DetectedThriveDataFolder { get; }

    public string? DetectedFullLogFileLocation { get; }

    public bool HasReportableCrash { get; }

    public bool ThriveWantsToOpenLauncher { get; }

    /// <summary>
    ///   For setting the LD_PRELOAD environment variable when starting Thrive
    /// </summary>
    public string? LDPreload { get; set; }

    /// <summary>
    ///   Extra flags to pass to Thrive when starting
    /// </summary>
    public IList<string>? ExtraThriveStartFlags { get; set; }

    public ErrorSuggestionType? ActiveErrorSuggestion { get; }

    /// <summary>
    ///   Exit code for last finished Thrive run
    /// </summary>
    public int ExitCode { get; }

    public IPlayableVersion? PlayedThriveVersion { get; }

    /// <summary>
    ///   Set to true when launched in seamless mode to enable some specific features related to that
    /// </summary>
    public bool LaunchedInSeamlessMode { get; set; }

    /// <summary>
    ///   Contains an exception from Thrive output if Thrive output an unhandled exception log message. Null otherwise.
    /// </summary>
    public string? UnhandledThriveException { get; }

    /// <summary>
    ///   Set to a path to a file if the Thrive output is detected to contain a message about a created dump file
    /// </summary>
    public string? DetectedCrashDumpOutputLocation { get; }

    public void StartThrive(IPlayableVersion version, bool applyCommandLineCustomizations,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Quit Thrive if currently running
    /// </summary>
    /// <returns>True if an active Thrive process was told to quit</returns>
    public bool QuitThrive();

    /// <summary>
    ///   Clears the game output. Useful for starting a new install with clean output
    /// </summary>
    public void ClearOutput();

    public void ClearDetectedCrashes();

    public IEnumerable<ReportableCrash> GetAvailableCrashesToReport();
}
