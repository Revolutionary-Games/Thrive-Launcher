namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LauncherThriveShared;
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
    private readonly ILauncherPaths launcherPaths;
    private readonly ILauncherOptions launcherOptions;
    private readonly ILauncherTranslations launcherTranslations;

    private readonly ObservableValue<bool> runningObservable = new(false);
    private readonly ObservableValue<bool> truncatedObservable = new(false);

    private CancellationTokenSource? playCancellationSource;
    private CancellationToken playCancellation = CancellationToken.None;

    private int startCounter;

    private int firstLinesToKeep;
    private int lastLinesToKeep;

    private bool readingUnhandledException;
    private List<string>? unhandledException;

    // This variable makes sure error is read from the right stream to reduce the amount of interleaving with other
    // log output
    private bool unhandledExceptionIsInErrorOut;

    private bool thriveProperlyStarted;

    /// <summary>
    ///   Used to match start info file with
    /// </summary>
    private Guid? thriveStartId;

    private StoreVersionInfo? currentStoreVersionInfo;

    private Thread? thriveRunnerThread;

    public ThriveRunner(ILogger<ThriveRunner> logger, ILauncherSettingsManager settingsManager,
        IThriveInstaller thriveInstaller, IStoreVersionDetector storeVersionDetector, ILauncherPaths launcherPaths,
        ILauncherOptions launcherOptions, ILauncherTranslations launcherTranslations)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;
        this.thriveInstaller = thriveInstaller;
        this.storeVersionDetector = storeVersionDetector;
        this.launcherPaths = launcherPaths;
        this.launcherOptions = launcherOptions;
        this.launcherTranslations = launcherTranslations;
    }

    public ObservableCollection<ThrivePlayMessage> PlayMessages { get; } = new();
    public ObservableCollection<ThriveOutputMessage> ThriveOutput { get; } = new();
    public ObservableCollection<ThriveOutputMessage> ThriveOutputTrailing { get; } = new();

    public IObservable<bool> OutputTruncatedObservable => truncatedObservable;

    public bool OutputTruncated => truncatedObservable.Value;

    public IObservable<bool> ThriveRunningObservable => runningObservable;

    public bool ThriveRunning => runningObservable.Value;

    public bool ThriveStartedCorrectly => thriveProperlyStarted;

    public string? DetectedThriveDataFolder { get; private set; }

    public string? DetectedFullLogFileLocation { get; private set; }

    public bool HasReportableCrash { get; private set; }
    public bool HasNonReportableExit { get; private set; }
    public bool ThriveWantsToOpenLauncher { get; private set; }

    public string? LDPreload { get; set; }
    public IList<string>? ExtraThriveStartFlags { get; set; }
    public ErrorSuggestionType? ActiveErrorSuggestion { get; private set; }

    public int ExitCode { get; private set; } = -42;
    public IPlayableVersion? PlayedThriveVersion { get; private set; }

    public string? UnhandledThriveException
    {
        get
        {
            if (unhandledException == null || unhandledException.Count < 1)
                return null;

            return string.Join("\n", unhandledException);
        }
    }

    public string? DetectedCrashDumpOutputLocation { get; private set; }

    public bool LaunchedInSeamlessMode { get; set; }

    public static IEnumerable<(string File, DateTime ModifiedAt)> GetCrashDumpsInFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<(string File, DateTime ModifiedAt)>();

        var dumps = new List<(string File, DateTime ModifiedAt)>();

        foreach (var dump in Directory.EnumerateFiles(folder, "*.dmp", SearchOption.AllDirectories))
        {
            dumps.Add((dump, new FileInfo(dump).LastWriteTimeUtc));
        }

        return dumps.OrderByDescending(t => t.ModifiedAt);
    }

    public void StartThrive(IPlayableVersion version, bool applyCommandLineCustomizations,
        CancellationToken cancellationToken)
    {
        JoinRunnerThreadIfExists();

        ClearOutput();
        startCounter = 0;

        if (applyCommandLineCustomizations)
        {
            logger.LogDebug("Applying command line customizations to Thrive that is about to start");

            LDPreload = launcherOptions.GameLDPreload;

            ExtraThriveStartFlags = launcherOptions.ThriveExtraFlags is { Count: > 0 } ?
                launcherOptions.ThriveExtraFlags :
                null;
        }

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
            // Store versions are related to the launcher folder and not the usual installation folder
            thriveFolder = Path.GetFullPath(Path.Join(AppDomain.CurrentDomain.BaseDirectory, version.FolderName));
            currentStoreVersionInfo = storeVersionDetector.Detect();

            if (!currentStoreVersionInfo.IsStoreVersion)
            {
                // This is reset just in case here as otherwise the launcher starting can get into a terrible state
                runningObservable.Value = false;
                throw new Exception("We are playing a store version but we haven't detected the store");
            }

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

        var platform = PlatformUtilities.GetCurrentPlatform();
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

        StartThriveWithRunnerThread(version, thriveExecutable);
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

    public void ClearOutput()
    {
        PlayMessages.Clear();

        lock (ThriveOutput)
        {
            ThriveOutput.Clear();
            ThriveOutputTrailing.Clear();
        }

        truncatedObservable.Value = false;
        DetectedThriveDataFolder = null;
        DetectedFullLogFileLocation = null;
        ActiveErrorSuggestion = null;
        ThriveWantsToOpenLauncher = false;
        thriveProperlyStarted = false;

        ClearDetectedCrashes();
    }

    public void ClearDetectedCrashes()
    {
        HasReportableCrash = false;
        HasNonReportableExit = false;
        readingUnhandledException = false;
        unhandledExceptionIsInErrorOut = false;
        unhandledException = null;
        DetectedCrashDumpOutputLocation = null;
    }

    public IEnumerable<ReportableCrash> GetAvailableCrashesToReport()
    {
        var potentialException = UnhandledThriveException;

        if (!string.IsNullOrEmpty(potentialException))
        {
            yield return new ReportableCrashException(potentialException);
        }

        // If a dump was detected from the output of Thrive, that is always shown first as the latest crash
        string? latestCrashDump = DetectedCrashDumpOutputLocation;

        if (!string.IsNullOrEmpty(latestCrashDump) && File.Exists(latestCrashDump))
        {
            logger.LogInformation("Saw latest crash dump from Thrive output: {LatestCrashDump}", latestCrashDump);
            yield return new ReportableCrashDump(latestCrashDump, new FileInfo(latestCrashDump).LastWriteTimeUtc, true);
        }

        var folder = DetectedThriveDataFolder;

        // The detected folder is the data folder, which is the parent folder for the crashes folder
        if (folder != null)
            folder = Path.Join(folder, LauncherConstants.ThriveCrashesFolderName);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            folder = launcherPaths.ThriveDefaultCrashesFolder;

        // These are already in sorted order, so we don't need to sort here
        foreach (var (dumpFile, time) in GetCrashDumpsInFolder(folder))
        {
            // Skip duplicates, as this was already output above
            if (latestCrashDump != null && Path.GetFullPath(latestCrashDump) == Path.GetFullPath(dumpFile))
            {
                continue;
            }

            yield return new ReportableCrashDump(dumpFile, time);
        }
    }

    private void JoinRunnerThreadIfExists()
    {
        if (thriveRunnerThread != null)
        {
            logger.LogInformation("Joining previous Thrive runner thread");
            thriveRunnerThread.Join(TimeSpan.FromSeconds(30));
            thriveRunnerThread = null;
        }
    }

    private void StartThriveWithRunnerThread(IPlayableVersion version, string thriveExecutable)
    {
        ++startCounter;

        // ReSharper disable once MethodSupportsCancellation
        thriveRunnerThread = new Thread(() => RunThriveExecutable(thriveExecutable, version, playCancellation).Wait())
        {
            // Even if this is running we want this process to be able to quit
            IsBackground = true,
        };
        thriveRunnerThread.Start();
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

            OnThriveExited(null, e, version, runTime.Elapsed, thriveExecutable);
            return;
        }

        OnThriveExited(result.ExitCode, null, version, runTime.Elapsed, thriveExecutable);
    }

    private void SetLaunchArgumentsAndEnvironment(ProcessStartInfo runInfo)
    {
        if (LDPreload != null)
        {
            logger.LogInformation("LD_PRELOAD for Thrive set to: {LDPreload}", LDPreload);
            runInfo.Environment["LD_PRELOAD"] = LDPreload;
        }
        else
        {
            logger.LogDebug("No LD_PRELOAD specified so passing empty one to the child process");
            runInfo.Environment["LD_PRELOAD"] = string.Empty;
        }

        var settings = settingsManager.Settings;

        if (settings.DisableThriveVideos)
            runInfo.ArgumentList.Add("--thrive-disable-videos");

        if (settings.ForceOpenGlMode)
        {
            runInfo.ArgumentList.Add("--rendering-driver");
            runInfo.ArgumentList.Add("opengl3");

            // Could also allow "opengl3_es" option
        }

        if (settings.OverrideAudioLatency)
        {
            logger.LogInformation("Setting Godot audio latency to: {Latency}", settings.AudioLatencyMilliseconds);

            runInfo.ArgumentList.Add("--audio-output-latency");
            runInfo.ArgumentList.Add(settings.AudioLatencyMilliseconds.ToString(CultureInfo.InvariantCulture));
        }

        if (currentStoreVersionInfo != null)
        {
            // Pass arguments about the store version to Thrive for it to show correct information
            // This is needed because itch builds don't have anything special on the Thrive side to them
            runInfo.ArgumentList.Add(
                $"{ThriveLauncherSharedConstants.THRIVE_LAUNCHER_STORE_PREFIX}{currentStoreVersionInfo.StoreName}");
        }

        runInfo.ArgumentList.Add(ThriveLauncherSharedConstants.OPENED_THROUGH_LAUNCHER_OPTION);

        // If we are going to close our window, tell that to Thrive
        // Or if we launched in seamless mode
        if (settings.CloseLauncherOnGameStart || settings.CloseLauncherAfterGameExit || LaunchedInSeamlessMode)
        {
            logger.LogDebug("Passing the launcher is hidden flag to Thrive");
            runInfo.ArgumentList.Add(ThriveLauncherSharedConstants.OPENING_LAUNCHER_IS_HIDDEN);
        }

        // Generate a new launch ID to make sure they are all unique. This is an alternative to detecting Thrive
        // startup from the output
        thriveStartId = Guid.NewGuid();

        runInfo.ArgumentList.Add($"{ThriveLauncherSharedConstants.THRIVE_LAUNCH_ID_PREFIX}{thriveStartId}");

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

    private void OnThriveExited(int? exitCode, Exception? runFailException, IPlayableVersion version, TimeSpan elapsed,
        string executable)
    {
        PlayedThriveVersion = version;

        logger.LogDebug("Detected log file ({DetectedFullLogFileLocation}) and data folder " +
            "from game output: {DetectedThriveDataFolder}", DetectedFullLogFileLocation, DetectedThriveDataFolder);

        if (DetectedFullLogFileLocation == null || DetectedThriveDataFolder == null)
            logger.LogWarning("No log file (or data folder) location could be detected from game output");

        if (!thriveProperlyStarted)
        {
            // Check if Thrive startup file can be read and determine based on that if Thrive did in fact start
            var file = DetectedThriveDataFolder == null ?
                launcherPaths.ThriveDefaultStartUpFile :
                Path.Join(DetectedThriveDataFolder, ThriveLauncherSharedConstants.LATEST_START_INFO_FILE_NAME);

            thriveProperlyStarted = CheckThriveStartupFile(file);
        }

        ThriveWantsToOpenLauncher =
            AllGameOutput().Any(m => m.Contains(ThriveLauncherSharedConstants.REQUEST_LAUNCHER_OPEN));
        var userRequestedQuit = AllGameOutput().Any(m => m.Contains(ThriveLauncherSharedConstants.USER_REQUESTED_QUIT));

        // TODO: detection for restart request

        if (ThriveWantsToOpenLauncher)
            logger.LogInformation("Thrive wants the launcher to be shown");

        if (exitCode != null)
        {
            OnNormalOutput($"Child process exited with code {exitCode}");
            ExitCode = exitCode.Value;
        }
        else
        {
            ExitCode = -2;
        }

        // Restart Thrive if it didn't start correctly (and the user didn't request the close)
        if (!thriveProperlyStarted && version.SupportsStartupDetection && !userRequestedQuit &&
            !ThriveWantsToOpenLauncher)
        {
            logger.LogWarning("Thrive was not detected as properly started");

            if (settingsManager.Settings.EnableThriveAutoRestart)
            {
                if (startCounter - 1 < LauncherConstants.ThriveStartupFailureRetries)
                {
                    logger.LogInformation("Will attempt to retry starting Thrive (start count: {StartCounter})",
                        startCounter);

                    PlayMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.ThriveRunRetry, startCounter + 1));

                    AddLogLine("Restarting Thrive due to detected startup failure", true);

                    // As we are running on the runner thread, we can't *really* join ourselves here
                    // JoinRunnerThreadIfExists();
                    StartThriveWithRunnerThread(version, executable);
                    return;
                }

                logger.LogWarning("Ran out of Thrive start retries");
            }
            else
            {
                logger.LogInformation("Auto retry of running Thrive is disabled in options");
            }
        }

        if (elapsed < LauncherConstants.RequiredRuntimeBeforeGameStartAdviceDisappears)
        {
            if (!thriveProperlyStarted && !userRequestedQuit)
            {
                logger.LogInformation("Thrive only ran for: {Elapsed}, showing startup fail advice", elapsed);

                ActiveErrorSuggestion = ErrorSuggestionType.ExitedQuickly;
            }
            else
            {
                logger.LogInformation("Thrive ran for a short time but output the startup success " +
                    "(or user quit request message), not showing short duration run advice");
            }
        }
        else
        {
            logger.LogDebug("Not showing startup fail advice as elapsed time is {Elapsed}", elapsed);
        }

        // TODO: this constant might be totally wrong now
        if (exitCode == -1073741515)
        {
            ActiveErrorSuggestion = ErrorSuggestionType.MissingDll;
        }

        DetectCrashDumpFromOutput();
        bool crashDumpsExist = GetAvailableCrashesToReport().Any();

        if (crashDumpsExist)
            logger.LogInformation("Thrive has generated crash dump(s)");

        if (runFailException == null && exitCode == 0 && unhandledException == null &&
            DetectedCrashDumpOutputLocation == null)
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
                HasNonReportableExit = true;

                // As this is a core launcher issue, this is set to true here to show the launcher in seamless mode
                HasReportableCrash = true;
            }
            else if (unhandledException != null)
            {
                HasReportableCrash = true;
                OnErrorOutput("Thrive has encountered an unhandled exception, please report this to us. " +
                    "In the future there will be support for automatically reporting these crashes.");
            }
            else
            {
                if (crashDumpsExist)
                {
                    // Detected crash dumps that should be reportable
                    HasReportableCrash = true;
                }
                else
                {
                    // Marking there to be reportable crashes here, would show the launcher in seamless mode even if
                    // there's nothing useful to do in the launcher (and with 0.6.6 Thrive always crashes on shutdown
                    // so the launcher would show a bunch when not needed).
                    logger.LogInformation("Thrive exited non-successfully but no crash dumps exist");
                    HasNonReportableExit = true;
                }
            }
        }

        if (crashDumpsExist && DetectedCrashDumpOutputLocation == null)
        {
            OnNormalOutput(launcherTranslations.CrashDumpsDetectedAdvice);
            HasReportableCrash = true;
        }

        // This is set at the end to allow the handler to inspect the error conditions
        runningObservable.Value = false;
    }

    private void DetectCrashDumpFromOutput()
    {
        DetectedCrashDumpOutputLocation = null;

        foreach (var outputLine in AllGameOutput())
        {
            var match = LauncherConstants.CrashDumpRegex.Match(outputLine);

            if (!match.Success)
                continue;

            var dump = match.Groups[1].Value;

            if (!File.Exists(dump))
            {
                logger.LogWarning("Game printed out non-existent crash dump file: {Dump}", dump);
            }
            else
            {
                logger.LogInformation("Detected crash dump created by the game: {Dump}", dump);
                DetectedCrashDumpOutputLocation = dump;
                break;
            }
        }
    }

    private IEnumerable<string> AllGameOutput()
    {
        lock (ThriveOutput)
        {
            foreach (var message in ThriveOutput)
                yield return message.Message;

            foreach (var message in ThriveOutputTrailing)
                yield return message.Message;
        }
    }

    private void AddLogLine(string line, bool error)
    {
        var outputObject = new ThriveOutputMessage(line, error);

        lock (ThriveOutput)
        {
            DetectUnhandledExceptionOutput(line, error);

            if (ThriveOutput.Count < firstLinesToKeep)
            {
                ThriveOutput.Add(outputObject);

                // Should be fine to only detect this from the first log lines
                DetectThriveDataFoldersFromOutput(line);

                if (!thriveProperlyStarted && line.Contains(ThriveLauncherSharedConstants.STARTUP_SUCCEEDED_MESSAGE))
                {
                    logger.LogInformation("Thrive detected as properly started");
                    thriveProperlyStarted = true;
                }

                return;
            }

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

                // Performance-wise this is fine for us but the listeners listening to this might be pretty
                // expensive to run...
                ThriveOutputTrailing.RemoveAt(0);
            }
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

                logger.LogDebug("Detected Thrive log location as: {DetectedFullLogFileLocation}, and data folder: " +
                    "{DetectedThriveDataFolder}",
                    DetectedFullLogFileLocation, DetectedThriveDataFolder);
            }
        }
    }

    private void DetectUnhandledExceptionOutput(string line, bool isErrorOut)
    {
        if (readingUnhandledException)
        {
            if (unhandledException == null)
                throw new Exception("Logic error in not setting unhandled exception storage list");

            if (line.Contains(LauncherConstants.EndOfUnhandledExceptionLogging))
            {
                readingUnhandledException = false;
            }
            else if (unhandledExceptionIsInErrorOut != isErrorOut)
            {
                // Wrong output, ignore
                return;
            }

            unhandledException.Add(line);
        }
        else if (line.Contains(LauncherConstants.StartOfUnhandledExceptionLogging) && !readingUnhandledException)
        {
            logger.LogDebug("Detected start of unhandled exception");

            unhandledException ??= new List<string>();
            unhandledException.Add(line);
            readingUnhandledException = true;
            unhandledExceptionIsInErrorOut = isErrorOut;
        }
    }

    private bool CheckThriveStartupFile(string file)
    {
        if (!File.Exists(file))
        {
            logger.LogInformation("Thrive startup info file doesn't exist at: {File}", file);
            return false;
        }

        try
        {
            using var stream = File.OpenRead(file);

            var info = JsonSerializer.Deserialize<ThriveStartInfo>(stream) ?? throw new NullDecodedJsonException();

            // The comparison shouldn't matter, but for extra bulletproofing this compares in a case-insensitive way
            if (thriveStartId != null &&
                info.StartId.ToLowerInvariant() == thriveStartId.ToString()!.ToLowerInvariant())
            {
                logger.LogInformation(
                    "Detected Thrive as correctly started at {Time} due to matching startup info file id: {StartId}",
                    info.StartedAt.ToString("G"), info.StartId);
                return true;
            }

            logger.LogDebug(
                "Read startup info file doesn't match what we expected, it was from: {Time} with id: {StartId}",
                info.StartedAt.ToString("G"), info.StartId);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Couldn't read Thrive startup file info from file: {File}", file);
            return false;
        }

        return false;
    }

    /// <summary>
    ///   Marks some Steam related stderr output as not an error for nicer output viewing
    /// </summary>
    /// <param name="line">The line to check</param>
    /// <returns>True if the output matches non-serious Steam output patterns</returns>
    private bool IsNonErrorSteamOutput(string line)
    {
        if (line.StartsWith("[S_API]"))
            return true;

        if (line.StartsWith("Setting breakpad"))
            return true;

        if (line.StartsWith("SteamInternal"))
            return true;

        return false;
    }
}
