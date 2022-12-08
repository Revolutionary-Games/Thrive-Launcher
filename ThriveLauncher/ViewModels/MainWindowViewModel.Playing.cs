namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using DynamicData;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using SharedBase.Utilities;

/// <summary>
///   This partial class handles everything related to the setting up and playing Thrive (and the related popup)
/// </summary>
public partial class MainWindowViewModel
{
    private readonly List<IDisposable> runnerObservers = new();

    private bool currentlyPlaying;
    private bool canCancelPlaying;
    private bool showCloseButtonOnPlayPopup;

    private bool thriveIsRunning;
    private bool thriveOutputIsTruncated;
    private bool hasReportableCrash;

    private bool registeredInstallerCallbacks;
    private bool registeredRunnerCallbacks;

    private CancellationTokenSource? playActionCancellationSource;
    private CancellationToken playActionCancellation = CancellationToken.None;

    private string playingThrivePopupTitle = string.Empty;
    private string playPopupTopMessage = string.Empty;
    private string playPopupBottomMessage = string.Empty;

    private bool unsafeBuildConfirmationRequired;
    private DevBuildVersion? pendingBuildToPlay;

    private bool wantsWindowHidden;
    private bool launcherShouldClose;

    public bool CanPressPlayButton =>
        !CurrentlyPlaying && !string.IsNullOrEmpty(SelectedVersionToPlay) && !thriveIsRunning;

    public bool CurrentlyPlaying
    {
        get => currentlyPlaying;
        private set
        {
            if (currentlyPlaying == value)
                return;

            this.RaiseAndSetIfChanged(ref currentlyPlaying, value);
            this.RaisePropertyChanged(nameof(CanPressPlayButton));
        }
    }

    public bool ThriveIsRunning
    {
        get => thriveIsRunning;
        private set
        {
            if (thriveIsRunning == value)
                return;

            this.RaiseAndSetIfChanged(ref thriveIsRunning, value);
            this.RaisePropertyChanged(nameof(CanPressPlayButton));

            if (!value)
            {
                OnPlayingEnded();
            }
        }
    }

    public bool CanCancelPlaying
    {
        get => canCancelPlaying;
        private set => this.RaiseAndSetIfChanged(ref canCancelPlaying, value);
    }

    public bool ShowCloseButtonOnPlayPopup
    {
        get => showCloseButtonOnPlayPopup;
        private set => this.RaiseAndSetIfChanged(ref showCloseButtonOnPlayPopup, value);
    }

    public string PlayingThrivePopupTitle
    {
        get => playingThrivePopupTitle;
        private set => this.RaiseAndSetIfChanged(ref playingThrivePopupTitle, value);
    }

    public string PlayPopupTopMessage
    {
        get => playPopupTopMessage;
        private set => this.RaiseAndSetIfChanged(ref playPopupTopMessage, value);
    }

    public string PlayPopupBottomMessage
    {
        get => playPopupBottomMessage;
        private set => this.RaiseAndSetIfChanged(ref playPopupBottomMessage, value);
    }

    public bool UnsafeBuildConfirmationRequired
    {
        get => unsafeBuildConfirmationRequired;
        private set => this.RaiseAndSetIfChanged(ref unsafeBuildConfirmationRequired, value);
    }

    /// <summary>
    ///   True when the launcher window should be hidden (based on user preferences in the launcher settings)
    /// </summary>
    public bool WantsWindowHidden
    {
        get => wantsWindowHidden;
        private set => this.RaiseAndSetIfChanged(ref wantsWindowHidden, value);
    }

    /// <summary>
    ///   When this is set to true the launcher wants itself to be closed (based on non-default selectable options
    ///   by the user)
    /// </summary>
    public bool LauncherShouldClose
    {
        get => launcherShouldClose;
        private set
        {
            if (value == launcherShouldClose)
                return;

            this.RaiseAndSetIfChanged(ref launcherShouldClose, value);

            if (launcherShouldClose)
                logger.LogInformation($"{nameof(LauncherShouldClose)} has been set to true!");
        }
    }

    public ObservableCollection<string> PlayMessages { get; } = new();

    public ObservableCollection<FilePrepareProgress> InProgressPlayOperations { get; } = new();

    public ObservableCollection<ThriveOutputMessage> ThriveOutputFirstPart { get; } = new();

    public bool ThriveOutputIsTruncated
    {
        get => thriveOutputIsTruncated;
        private set => this.RaiseAndSetIfChanged(ref thriveOutputIsTruncated, value);
    }

    public ObservableCollection<ThriveOutputMessage> ThriveOutputLastPart => thriveRunner.ThriveOutputTrailing;

    public bool HasReportableCrash
    {
        get => hasReportableCrash;
        private set => this.RaiseAndSetIfChanged(ref hasReportableCrash, value);
    }

    /// <summary>
    ///   The whole output that we buffer, used for copying to the clipboard
    /// </summary>
    public List<string> WholeBufferedOutput
    {
        get
        {
            var data = ThriveOutputFirstPart
                .Concat(ThriveOutputLastPart).Select(o => o.IsError ? $"ERROR: {o.Message}" : o.Message).ToList();

            if (ThriveOutputIsTruncated)
                data.Add("This log is TRUNCATED, please see the Thrive log file for full output!");

            return data;
        }
    }

    public void TryToPlayThrive()
    {
        if (CurrentlyPlaying)
        {
            // Disallow starting again
            ShowNotice(Resources.CannotPlayThriveTitle, Resources.ThriveCurrentlyRunning);
            return;
        }

        var versionToPlay = SelectedVersionToPlay;

        var version = AvailableThriveVersions.Where(t => t.VersionObject.VersionName == versionToPlay)
            .Select(t => t.VersionObject)
            .FirstOrDefault();

        if (version == null)
        {
            logger.LogInformation("No version to play found, selected: {VersionToPlay}", versionToPlay);
            ShowNotice(Resources.CannotPlayThriveTitle, Resources.NoVersionSelected);
            return;
        }

        logger.LogInformation("Starting playing Thrive {VersionName}", version.VersionName);
        PlayingThrivePopupTitle = string.Format(Resources.PlayingTitle, version.VersionName);
        PlayPopupTopMessage = string.Empty;
        PlayPopupBottomMessage = string.Empty;
        CurrentlyPlaying = true;
        ShowCloseButtonOnPlayPopup = false;

        PlayMessages.Clear();
        InProgressPlayOperations.Clear();

        // Allow canceling the Thrive version setup and download
        CanCancelPlaying = true;

        playActionCancellationSource = new CancellationTokenSource();
        playActionCancellation = playActionCancellationSource.Token;

        RegisterInstallerMessageForwarders();

        StartPlayingThrive(version);
    }

    public void ClosePlayingPopup()
    {
        if (playActionCancellationSource == null)
        {
            logger.LogWarning("Playing has not been started by this view model, we can't cancel it properly");
        }
        else
        {
            if (!playActionCancellationSource.IsCancellationRequested)
            {
                playActionCancellationSource.Cancel();
                logger.LogInformation("User requested cancel of current playing of Thrive");
            }
        }

        thriveRunner.QuitThrive();

        logger.LogInformation("Closing play popup due to cancellation");
        CurrentlyPlaying = false;
        CanCancelPlaying = false;
    }

    public string GetFullOutputForClipboard()
    {
        logger.LogDebug("Start building full log output for copy");

        var stringBuilder = new StringBuilder(5000);

        if (!string.IsNullOrEmpty(PlayPopupTopMessage))
        {
            stringBuilder.Append(PlayPopupTopMessage);
            stringBuilder.Append("\n");
        }

        bool first = true;
        bool errorLineOutput = false;

        foreach (var playMessage in thriveRunner.PlayMessages)
        {
            if (!first)
                stringBuilder.Append("\n");

            first = false;

            stringBuilder.Append(FormatPlayMessage(playMessage));
        }

        foreach (var outputMessage in thriveRunner.ThriveOutput.Concat(thriveRunner.ThriveOutputTrailing))
        {
            if (!first)
                stringBuilder.Append("\n");

            first = false;

            if (outputMessage.IsError && !errorLineOutput)
            {
                stringBuilder.Append(
                    "Note: error lines may not match up when they happened in relation to normal output " +
                    "due to buffering.\n");
                stringBuilder.Append("Error lines are any lines received from the game's stderr output stream.\n");
                errorLineOutput = true;
            }

            if (outputMessage.IsError && !outputMessage.Message.Contains("ERROR"))
            {
                stringBuilder.Append("ERROR: ");
            }

            stringBuilder.Append(outputMessage.Message);
        }

        if (!string.IsNullOrEmpty(PlayPopupBottomMessage))
        {
            stringBuilder.Append("\n");
            stringBuilder.Append(PlayPopupBottomMessage);
        }

        if (thriveRunner.OutputTruncated)
        {
            stringBuilder.Append("\n");
            stringBuilder.Append(Resources.TruncatedGameOutputWarning);
        }

        if (thriveRunner.ThriveRunning)
            stringBuilder.Append("\nThrive is still running.");

        if (LauncherConstants.UsePlatformLineSeparatorsInCopiedLog)
            stringBuilder.MakeLineSeparatorsPlatformSpecific();

        logger.LogInformation("Copying currently in-memory game logs to clipboard (length: {Length})",
            stringBuilder.Length);

        return stringBuilder.ToString();
    }

    public void AcceptPlayUnsafeBuild()
    {
        UnsafeBuildConfirmationRequired = false;

        if (pendingBuildToPlay == null)
        {
            logger.LogError("No set build to play in unsafe build accepted");
            return;
        }

        if (playActionCancellation.IsCancellationRequested)
        {
            ReportRunFailure(Resources.ThriveRunCanceled);
            return;
        }

        logger.LogInformation("Accepted playing potentially unsafe build");
        backgroundExceptionNoticeDisplayer.HandleTask(CheckFilesAndStartThrive(pendingBuildToPlay));
        pendingBuildToPlay = null;
    }

    public void CancelPlayUnsafeBuild()
    {
        pendingBuildToPlay = null;
        UnsafeBuildConfirmationRequired = false;

        ReportRunFailure(Resources.PlayingUnsafeBuildWasCanceled, true);
    }

    private async void StartPlayingThrive(IPlayableVersion version)
    {
        logger.LogDebug("Clearing previous Thrive play logs");
        thriveRunner.ClearOutput();
        HasReportableCrash = false;

        if (version is DevBuildVersion devBuildVersion)
        {
            // For devbuild type builds we need to start retrieving the build info before we can check if we have it
            // installed
            Dispatcher.UIThread.Post(() =>
            {
                PlayPopupTopMessage = string.Format(Resources.GettingDevBuildInfoMessage,
                    SelectedDevBuildType.ToString());
            });

            DevBuildLauncherDTO build;
            try
            {
                build = await devCenterClient.FetchBuildWeWantToPlay() ??
                    throw new Exception(Resources.NoBuildToPlayFound);
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PlayPopupTopMessage = string.Format(Resources.ErrorGettingDevBuild, e.Message);
                });

                logger.LogWarning("Failed to get DevBuild to play");
                return;
            }

            if (playActionCancellation.IsCancellationRequested)
            {
                ReportRunFailure(Resources.ThriveRunCanceled);
                return;
            }

            logger.LogInformation("DevBuild we want to really play: {Id}", build.Id);
            devBuildVersion.ExactBuild = build;

            Dispatcher.UIThread.Post(() =>
            {
                PlayPopupTopMessage = string.Format(Resources.FoundDevBuildToPlayInfo, build.Id, build.BuildHash,
                    build.BuildOfTheDay, build.Branch);

                if (!string.IsNullOrWhiteSpace(build.Description))
                {
                    PlayPopupTopMessage = PlayPopupTopMessage + "\n" +
                        string.Format(Resources.FoundDevBuildToPlayDescription, build.Description);
                }
            });

            if (build.Anonymous && !build.Verified)
            {
                logger.LogInformation("DevBuild is an unsafe build, asking for confirmation first");

                pendingBuildToPlay = devBuildVersion;
                UnsafeBuildConfirmationRequired = true;
                return;
            }
        }
        else
        {
            PlayPopupTopMessage = string.Empty;
        }

        await CheckFilesAndStartThrive(version);
    }

    private async Task CheckFilesAndStartThrive(IPlayableVersion version)
    {
        logger.LogInformation("Checking that Thrive version is good to play or starting download for it");
        try
        {
            if (!await thriveInstaller.EnsureVersionIsDownloaded(version, playActionCancellation))
            {
                ReportRunFailure(Resources.VersionInstallationOrCheckFailed);
                return;
            }
        }
        catch (Exception e)
        {
            ReportRunFailure(string.Format(Resources.ThriveFolderPrepareFailed, e));
            return;
        }

        if (playActionCancellation.IsCancellationRequested)
        {
            ReportRunFailure(Resources.ThriveRunCanceled);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            logger.LogInformation("Thrive installation verified, starting Thrive next");

            // Disallow canceling while Thrive is running
            CanCancelPlaying = false;

            RegisterThriveRunnerListeners();

            thriveRunner.StartThrive(version, true, playActionCancellation);

            if (settingsManager.Settings.CloseLauncherOnGameStart)
            {
                backgroundExceptionNoticeDisplayer.HandleTask(CloseLauncherAfterStart());
            }
            else if (settingsManager.Settings.HideLauncherOnPlay)
            {
                backgroundExceptionNoticeDisplayer.HandleTask(HideLauncherAfterStart());
            }
        });
    }

    private void ReportRunFailure(string message, bool preserveTopMessage = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (preserveTopMessage)
            {
                PlayPopupTopMessage = PlayPopupTopMessage + "\n" + string.Format(Resources.ThriveRunError, message);
            }
            else
            {
                PlayPopupTopMessage = string.Format(Resources.ThriveRunError, message);
            }

            CanCancelPlaying = true;
            ShowCloseButtonOnPlayPopup = true;
        });
    }

    private void OnPlayingEnded()
    {
        logger.LogDebug("Thrive is no longer running, reported to view model");

        Dispatcher.UIThread.Post(() =>
        {
            ShowCloseButtonOnPlayPopup = true;
            CanCancelPlaying = true;
            HasReportableCrash = thriveRunner.HasReportableCrash;

            bool allowUnHide = true;

            // Update bottom advice tip if there's something to show
            if (thriveRunner.ActiveErrorSuggestion != null)
            {
                switch (thriveRunner.ActiveErrorSuggestion.Value)
                {
                    case ErrorSuggestionType.MissingDll:
                        PlayPopupBottomMessage = Resources.ErrorSuggestionForMissingDll;
                        break;
                    case ErrorSuggestionType.ExitedQuickly:
                        PlayPopupBottomMessage = Resources.ErrorSuggestionForStartupFailure;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else if (!thriveRunner.HasReportableCrash && !thriveRunner.ThriveWantsToOpenLauncher)
            {
                // Thrive ran without error, check the close after playing option if we want to close
                if (CloseLauncherAfterGameExit)
                {
                    logger.LogInformation(
                        "Closing launcher after playing Thrive without error as selected by the user");
                    LauncherShouldClose = true;
                    allowUnHide = false;
                }
            }

            if (allowUnHide)
            {
                backgroundExceptionNoticeDisplayer.HandleTask(CheckLauncherUnHide());
            }
        });
    }

    private void RegisterInstallerMessageForwarders()
    {
        if (registeredInstallerCallbacks)
            return;

        registeredInstallerCallbacks = true;

        thriveInstaller.InstallerMessages.CollectionChanged += OnInstallerMessagesChanged;
        thriveInstaller.InProgressOperations.CollectionChanged += OnInProgressOperationsChanged;
    }

    private void UnRegisterInstallerMessageForwarders()
    {
        if (!registeredInstallerCallbacks)
            return;

        registeredInstallerCallbacks = false;

        thriveInstaller.InstallerMessages.CollectionChanged -= OnInstallerMessagesChanged;
        thriveInstaller.InProgressOperations.CollectionChanged -= OnInProgressOperationsChanged;
    }

    private void OnInstallerMessagesChanged(object? sender,
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        if (!CurrentlyPlaying)
        {
            logger.LogWarning("Ignoring installer message as not currently playing");
            return;
        }

        logger.LogDebug("Redoing play messages due to changes to installer messages");

        // As this is just a list of strings we save the complicated code for the below collection and just redo
        // this each time
        PlayMessages.Clear();
        PlayMessages.AddRange(thriveInstaller.InstallerMessages.Select(FormatPlayMessage));
    }

    private void OnInProgressOperationsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!CurrentlyPlaying)
        {
            logger.LogWarning("Ignoring installer progress message as not currently playing");
            return;
        }

        logger.LogDebug("Installer in progress operation change type: {Action}, new: {NewItems}", args.Action,
            args.NewItems);

        InProgressPlayOperations.ApplyChangeFromAnotherCollection(args);
    }

    private void RegisterThriveRunnerListeners()
    {
        if (registeredRunnerCallbacks)
            return;

        registeredRunnerCallbacks = true;

        runnerObservers.Add(thriveRunner.ThriveRunningObservable.Subscribe(new LambdaBasedObserver<bool>(value =>
        {
            ThriveIsRunning = value;
        })));

        runnerObservers.Add(thriveRunner.OutputTruncatedObservable.Subscribe(new LambdaBasedObserver<bool>(value =>
        {
            ThriveOutputIsTruncated = value;
        })));

        // The runner's messages overwrite the folder setup messages as both aren't important to show at once
        thriveRunner.PlayMessages.CollectionChanged += OnPlayMessagesChanged;

        // Initial bunch of messages is never removed from so we can only append or redo the whole thing
        thriveRunner.ThriveOutput.CollectionChanged += OnThriveOutputChanged;

        // The second part of messages is exposed as the original object as whoever handles that should take
        // the partial updates into account for best performance
    }

    private void UnRegisterThriveRunnerListeners()
    {
        if (!registeredRunnerCallbacks)
            return;

        registeredRunnerCallbacks = false;

        foreach (var observer in runnerObservers)
        {
            observer.Dispose();
        }

        runnerObservers.Clear();

        thriveRunner.PlayMessages.CollectionChanged -= OnPlayMessagesChanged;

        thriveRunner.ThriveOutput.CollectionChanged -= OnThriveOutputChanged;
    }

    private void OnPlayMessagesChanged(object? sender,
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        if (!CurrentlyPlaying)
        {
            logger.LogWarning("Ignoring runner message as not currently playing");
            return;
        }

        logger.LogDebug("Redoing play messages due to changes to runner messages");

        PlayMessages.Clear();
        PlayMessages.AddRange(thriveRunner.PlayMessages.Select(FormatPlayMessage));
    }

    private void OnThriveOutputChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!CurrentlyPlaying)
        {
            logger.LogWarning("Ignoring runner output message as not currently playing");
            return;
        }

        // We don't want to create duplicate lists of this as this may be a bit big so instead we lock to
        // make sure the read and write to the object happen sensibly
        lock (ThriveOutputFirstPart)
        {
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ThriveOutputFirstPart.AddOrInsertRange(args.NewItems!.Cast<ThriveOutputMessage>(),
                        args.NewStartingIndex);

                    break;
                case NotifyCollectionChangedAction.Reset:
                    ThriveOutputFirstPart.Clear();
                    break;
            }
        }
    }

    private string FormatPlayMessage(ThrivePlayMessage message)
    {
        switch (message.MessageType)
        {
            case ThrivePlayMessage.Type.Downloading:
                return message.Format(Resources.DownloadingItem);
            case ThrivePlayMessage.Type.DownloadingFailed:
                return message.Format(Resources.DownloadError);
            case ThrivePlayMessage.Type.ExtractionFailed:
                return message.Format(Resources.ExtractError);
            case ThrivePlayMessage.Type.MissingThriveFolder:
                return message.Format(Resources.MissingThriveFolderError);
            case ThrivePlayMessage.Type.MissingThriveExecutable:
                return message.Format(Resources.MissingThriveExecutableError);
            case ThrivePlayMessage.Type.StartingThrive:
                return Resources.ThriveIsStarting;
            case ThrivePlayMessage.Type.ExtraStartFlags:
                return message.Format(Resources.ExtraThriveStartFlags);
            case ThrivePlayMessage.Type.RehydrationFailed:
                return message.Format(Resources.RehydrationFailed);
            case ThrivePlayMessage.Type.Rehydrating:
                return message.Format(Resources.RehydrationStarting);
            case ThrivePlayMessage.Type.ThriveRunRetry:
                return message.Format(Resources.ThriveRunRetryStarting);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task CloseLauncherAfterStart()
    {
        logger.LogInformation(
            "Closing the launcher window as launcher options specify to close after game start");

        // We have no relevant cancellation token here
        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(LauncherConstants.CloseDelayAfterAutoStart);

        if (!thriveRunner.ThriveRunning)
        {
            logger.LogWarning("Thrive has exited before we closed the launcher window, will not close the window");

            PlayPopupBottomMessage = Resources.ThriveLaunchFailurePreventedAutoClose;
            return;
        }

        LauncherShouldClose = true;
    }

    private async Task HideLauncherAfterStart()
    {
        logger.LogInformation("Hiding the launcher window after starting Thrive as configured");

        // We have no relevant cancellation token here
        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(LauncherConstants.MinimizeDelayAfterGameStart);

        if (!thriveRunner.ThriveRunning)
        {
            logger.LogWarning("Thrive has exited before we hid the launcher window, skipping");
            return;
        }

        WantsWindowHidden = true;
    }

    private async Task CheckLauncherUnHide()
    {
        if (!settingsManager.Settings.HideLauncherOnPlay)
            return;

        // We have no relevant cancellation token here
        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(LauncherConstants.RestoreDelayAfterGameEnd);

        if (!thriveRunner.ThriveRunning)
        {
            logger.LogInformation("Showing the launcher again after running Thrive");

            WantsWindowHidden = false;
        }
    }

    private async void DetectPlayingStatusFromBackend()
    {
        // We wait here to make sure that the window is registered to us to listen for stuff before we set up
        // everything
        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(LauncherConstants.DelayForBackendStateCheckOnStart);
        logger.LogDebug("Checking if launcher backend has active things we should know about");

        try
        {
            if (thriveRunner.ThriveRunning || thriveRunner.PlayMessages.Count > 0 ||
                thriveRunner.ThriveOutput.Count > 0)
            {
                logger.LogInformation("Restoring state from backend for Thrive runner");

                CurrentlyPlaying = true;

                logger.LogDebug("Restoring play messages from runner");
                PlayMessages.Clear();
                PlayMessages.AddRange(thriveRunner.PlayMessages.Select(FormatPlayMessage));

                ThriveOutputFirstPart.Clear();
                ThriveOutputFirstPart.AddRange(thriveRunner.ThriveOutput);

                // Output last part has to be updated by the GUI to make sure it is showing the latest data

                RegisterThriveRunnerListeners();

                if (!thriveRunner.ThriveRunning)
                {
                    CanCancelPlaying = true;
                    ThriveIsRunning = false;

                    // Make sure this is called, which it isn't necessarily due to the default value of ThriveIsRunning
                    // variable
                    OnPlayingEnded();
                }
                else
                {
                    ThriveIsRunning = true;
                }

                ThriveOutputIsTruncated = thriveRunner.OutputTruncated;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to properly restore status of the launcher view model from backend");
        }
    }
}
