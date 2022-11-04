namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Models;
using SharedBase.Utilities;

public class ThriveRunner : IThriveRunner
{
    private readonly ILogger<ThriveRunner> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly IThriveInstaller thriveInstaller;
    private readonly IStoreVersionDetector storeVersionDetector;

    private readonly ObservableValue<bool> runningObservable = new(false);
    private readonly ObservableValue<bool> truncatedObservable = new(false);

    private CancellationTokenSource? playCancellationSource;
    private CancellationToken playCancellation = CancellationToken.None;

    private int firstLinesToKeep;
    private int lastLinesToKeep;

    private bool readingUnhandledException;
    private List<string>? unhandledException;

    private StoreVersionInfo? currentStoreVersionInfo;

    private Thread? thriveRunnerThread;

    public ThriveRunner(ILogger<ThriveRunner> logger, ILauncherSettingsManager settingsManager,
        IThriveInstaller thriveInstaller, IStoreVersionDetector storeVersionDetector)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;
        this.thriveInstaller = thriveInstaller;
        this.storeVersionDetector = storeVersionDetector;
    }

    public ObservableCollection<ThrivePlayMessage> PlayMessages { get; } = new();
    public ObservableCollection<ThriveOutputMessage> ThriveOutput { get; } = new();
    public ObservableCollection<ThriveOutputMessage> ThriveOutputTrailing { get; } = new();

    public IObservable<bool> OutputTruncatedObservable => truncatedObservable;

    public bool OutputTruncated => truncatedObservable.Value;

    public IObservable<bool> ThriveRunningObservable => runningObservable;

    public bool ThriveRunning => runningObservable.Value;

    public string? DetectedThriveDataFolder { get; private set; }

    public string? DetectedFullLogFileLocation { get; private set; }

    public bool HasReportableCrash { get; private set; }

    public string? LDPreload { get; set; }
    public IList<string>? ExtraThriveStartFlags { get; set; }
    public ErrorSuggestionType? ActiveErrorSuggestion { get; private set; }

    public void StartThrive(IPlayableVersion version, CancellationToken cancellationToken)
    {
        if (thriveRunnerThread != null)
        {
            logger.LogInformation("Joining previous Thrive runner thread");
            thriveRunnerThread.Join(TimeSpan.FromSeconds(30));
            thriveRunnerThread = null;
        }

        PlayMessages.Clear();
        ThriveOutput.Clear();
        ThriveOutputTrailing.Clear();
        truncatedObservable.Value = false;
        HasReportableCrash = false;
        DetectedThriveDataFolder = null;
        DetectedFullLogFileLocation = null;
        readingUnhandledException = false;
        unhandledException = null;
        ActiveErrorSuggestion = null;

        // Update our copy of the settings variables
        firstLinesToKeep = settingsManager.Settings.BeginningKeptGameOutput;
        lastLinesToKeep = settingsManager.Settings.LastKeptGameOutput;

        cancellationToken.ThrowIfCancellationRequested();

        runningObservable.Value = true;

        // We need to also be able to cancel things ourselves, so we create this one level of indirection
        playCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        playCancellation = playCancellationSource.Token;

        string thriveFolder;

        if (version is StoreVersion)
        {
            // Store versions are related to the launcher folder and not the usual install folder
            thriveFolder = Path.GetFullPath(Path.Join(AppDomain.CurrentDomain.BaseDirectory, version.FolderName));
            currentStoreVersionInfo = storeVersionDetector.Detect();

            if (!currentStoreVersionInfo.IsStoreVersion)
                throw new Exception("We are playing a store version but we haven't detected the store");

            logger.LogDebug("Playing {StoreName} store version", currentStoreVersionInfo.StoreName);
        }
        else
        {
            thriveFolder = Path.Join(thriveInstaller.BaseInstallFolder, version.FolderName);
            currentStoreVersionInfo = null;
        }

        logger.LogInformation("Trying to start Thrive from folder: {ThriveFolder}", thriveFolder);

        if (!Directory.Exists(thriveFolder))
        {
            PlayMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.MissingThriveFolder, thriveFolder));
            runningObservable.Value = false;
            return;
        }

        var platform = thriveInstaller.GetCurrentPlatform();
        var executableFolder =
            thriveInstaller.FindThriveExecutableFolderInVersion(thriveFolder, platform);

        string? thriveExecutable = null;

        if (executableFolder != null)
        {
            if (thriveInstaller.ThriveExecutableExistsInFolder(executableFolder, platform))
            {
                thriveExecutable = Path.Join(executableFolder, ThriveProperties.GetThriveExecutableName(platform));
            }
        }

        // If we didn't find the executable or accidentally found a folder, give an error and don't try to run that
        if (executableFolder == null || thriveExecutable == null || Directory.Exists(thriveExecutable))
        {
            PlayMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.MissingThriveExecutable, thriveFolder));
            runningObservable.Value = false;
            return;
        }

        logger.LogDebug("Thrive executable is: {ThriveExecutable}", thriveExecutable);

        // ReSharper disable once MethodSupportsCancellation
        thriveRunnerThread = new Thread(() => RunThriveExecutable(thriveExecutable, version, playCancellation).Wait())
        {
            // Even if this is running we want this process to be able to quit
            IsBackground = true,
        };
        thriveRunnerThread.Start();
    }

    public bool QuitThrive()
    {
        if (playCancellationSource is { IsCancellationRequested: false })
        {
            logger.LogWarning("Canceling running Thrive due to cancel request");
            playCancellationSource.Cancel();

            // We rely on the Thrive runner thread to properly notice the cancellation and set the running flag to
            // false, see OnThriveExited for why we do that
            return true;
        }

        return false;
    }

    private async Task RunThriveExecutable(string thriveExecutable, IPlayableVersion version,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.GetDirectoryName(thriveExecutable) ?? string.Empty;

        var runInfo = new ProcessStartInfo(thriveExecutable)
        {
            WorkingDirectory = workingDirectory,
        };

        SetLaunchArgumentsAndEnvironment(runInfo);

        logger.LogInformation(
            "Starting {ThriveExecutable} with working directory: {WorkingDirectory}, arguments: {ArgumentList}",
            thriveExecutable, workingDirectory, string.Join(" ", runInfo.ArgumentList));

        PlayMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.StartingThrive));

        logger.LogDebug("Will start Thrive process next");
        var runTime = new Stopwatch();
        runTime.Start();

        ProcessRunHelpers.ProcessResult result;
        try
        {
            // We pass in empty stdin to the process to make it not inherit our own stdin
            result = await ProcessRunHelpers.RunProcessWithStdInAndOutputStreamingAsync(runInfo, cancellationToken,
                new string[] { }, OnNormalOutput, OnErrorOutput);

            // TODO: do we somehow need to get the guard back in here that checked after start that the process is
            // alive or the exited callback has been triggered?

            if (!result.Exited)
                throw new Exception("Process did not end in an expected way");

            logger.LogInformation("Thrive process exited with code: {ExitCode}, total runtime: {Elapsed}",
                result.ExitCode, runTime.Elapsed);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Thrive failed to run");

            OnThriveExited(workingDirectory, null, e, version, runTime.Elapsed);
            return;
        }

        // onGameEnded(signal, closeContainer, version.releaseNum, storeInfo.store, status,elapsed, gameOutput, detectedLogFile);
        OnThriveExited(workingDirectory, result.ExitCode, null, version, runTime.Elapsed);
    }

    private void SetLaunchArgumentsAndEnvironment(ProcessStartInfo runInfo)
    {
        if (LDPreload != null)
        {
            // TODO: test that this works
            logger.LogInformation("LD_PRELOAD for Thrive set to: {LDPreload}", LDPreload);
            runInfo.Environment["LD_PRELOAD"] = LDPreload;
        }
        else
        {
            logger.LogDebug("No LD_PRELOAD specified so passing empty one to the child process");
            runInfo.Environment["LD_PRELOAD"] = "";
        }

        if (settingsManager.Settings.DisableThriveVideos)
            runInfo.ArgumentList.Add("--thrive-disable-videos");

        if (settingsManager.Settings.ForceGles2Mode)
        {
            runInfo.ArgumentList.Add("--video-driver");
            runInfo.ArgumentList.Add("GLES2");
        }

        if (currentStoreVersionInfo != null)
        {
            // TODO: pass arguments about the store version to Thrive for it to show correct information
            // This is needed because itch builds don't have anything special on the Thrive side to them
        }

        if (ExtraThriveStartFlags is { Count: > 0 })
        {
            var extraFlagsString = string.Join(" ", ExtraThriveStartFlags);
            logger.LogInformation("Extra thrive parameters: {ExtraThriveStartFlags}",
                extraFlagsString);

            foreach (var extraThriveStartFlag in ExtraThriveStartFlags)
            {
                runInfo.ArgumentList.Add(extraThriveStartFlag);
            }

            PlayMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.ExtraStartFlags, extraFlagsString));
        }
    }

    private void OnThriveExited(string thriveFolder, int? exitCode, Exception? runFailException,
        IPlayableVersion version, TimeSpan elapsed)
    {
        if (exitCode != null)
        {
            OnNormalOutput($"Child process exited with code {exitCode}");
        }

        if (elapsed < LauncherConstants.RequiredRuntimeBeforeGameStartAdviceDisappears)
        {
            logger.LogInformation("Thrive only ran for: {Elapsed}, showing startup fail advice", elapsed);

            ActiveErrorSuggestion = ErrorSuggestionType.ExitedQuickly;
        }

        // TODO: this constant might be totally wrong now
        if (exitCode == -1073741515)
        {
            ActiveErrorSuggestion = ErrorSuggestionType.MissingDll;
        }

        if (runFailException == null && exitCode == 0 && unhandledException == null)
        {
            logger.LogDebug("Thrive exited successfully");
            OnNormalOutput("Thrive has exited normally (exit code 0).");
        }
        else
        {
            logger.LogWarning("Thrive didn't run successfully (crashed or another problem occurred)");
            OnNormalOutput("Thrive exited abnormally with an error");

            if (runFailException != null)
            {
                OnErrorOutput($"Running Thrive has failed with exception: {runFailException.Message}");
                logger.LogInformation(runFailException, "Thrive failed to run due to an exception");
            }
            else if (unhandledException != null)
            {
                // TODO: implement reporting unhandled exceptions as crashes
                OnErrorOutput("Thrive has encountered an unhandled exception, please report this to us. " +
                    "In the future there will be support for automatically reporting these crashes.");
            }
            else
            {
                // Detect crash dumps
                // HasReportableCrash = true;

                throw new NotImplementedException();
            }
        }

        // This is set at the end to allow the handler to inspect the error conditions
        runningObservable.Value = false;
    }

    private void AddLogLine(string line, bool error)
    {
        var outputObject = new ThriveOutputMessage(line, error);

        if (ThriveOutput.Count < firstLinesToKeep)
        {
            ThriveOutput.Add(outputObject);

            // Should be fine to only detect this from the first log lines
            DetectThriveDataFoldersFromOutput(line);

            return;
        }

        DetectUnhandledExceptionOutput(line);

        // TODO: detection for restart and exiting to launcher

        // Performance of this collection type seems fine for now, but when getting stuff to the GUI, the GUI
        // needs to buffer removes

        ThriveOutputTrailing.Add(outputObject);

        if (ThriveOutputTrailing.Count > lastLinesToKeep)
        {
            if (!truncatedObservable.Value)
            {
                logger.LogDebug("Output from Thrive is so long that it is truncated");
                truncatedObservable.Value = true;
            }

            // Performance-wise this is fine for us but the listeners listening to this might be pretty expensive to
            // run...
            ThriveOutputTrailing.RemoveAt(0);
        }
    }

    private void OnNormalOutput(string line)
    {
        AddLogLine(line, false);
    }

    private void OnErrorOutput(string line)
    {
        if (IsNonErrorSteamOutput(line))
        {
            OnNormalOutput(line);
            return;
        }

        AddLogLine(line, true);
    }

    private void DetectThriveDataFoldersFromOutput(string line)
    {
        if (DetectedFullLogFileLocation == null)
        {
            var match = LauncherConstants.ThriveOutputLogLocation.Match(line);

            if (match.Success)
            {
                DetectedFullLogFileLocation = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
                DetectedThriveDataFolder = Path.GetDirectoryName(match.Groups[1].Value);

                logger.LogDebug(
                    "Detected Thrive log location as: {DetectedFullLogFileLocation}, and data folder: " +
                    "{DetectedThriveDataFolder}",
                    DetectedFullLogFileLocation, DetectedThriveDataFolder);
            }
        }
    }

    private void DetectUnhandledExceptionOutput(string line)
    {
        if (readingUnhandledException)
        {
            if (unhandledException == null)
                throw new Exception("Logic error in not setting unhandled exception storage list");

            unhandledException.Add(line);

            if (line.Contains(LauncherConstants.EndOfUnhandledExceptionLogging))
            {
                readingUnhandledException = false;
            }
        }
        else if (line.Contains(LauncherConstants.StartOfUnhandledExceptionLogging))
        {
            // TODO: test that the unhandled exception finding works correctly
            logger.LogDebug("Detected start of unhandled exception");

            unhandledException ??= new List<string>();
            unhandledException.Add(line);
        }
    }

    /// <summary>
    ///   Marks some Steam related stderr output as not an error for nicer output viewing
    /// </summary>
    /// <param name="line">The line to check</param>
    /// <returns>True if the output matches non-serious Steam output patterns</returns>
    private bool IsNonErrorSteamOutput(string line)
    {
        // TODO: implement this
        return false;
    }
}

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

    /// <summary>
    ///   For setting the LD_PRELOAD environment variable when starting Thrive
    /// </summary>
    public string? LDPreload { get; set; }

    /// <summary>
    ///   Extra flags to pass to Thrive when starting
    /// </summary>
    public IList<string>? ExtraThriveStartFlags { get; set; }

    public ErrorSuggestionType? ActiveErrorSuggestion { get; }

    public void StartThrive(IPlayableVersion version, CancellationToken cancellationToken);

    /// <summary>
    ///   Quit Thrive if currently running
    /// </summary>
    /// <returns>True if an active Thrive process was told to quit</returns>
    public bool QuitThrive();
}
