namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using DynamicData;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using SharedBase.Utilities;

/// <summary>
///   This partial class handles everything related to the setting up and playing Thrive (and the related popup)
/// </summary>
public partial class MainWindowViewModel
{
    private bool currentlyPlaying;
    private bool canCancelPlaying;
    private bool showCloseButtonOnPlayPopup;

    private bool thriveIsRunning;
    private bool thriveOutputIsTruncated;

    private bool registeredInstallerCallbacks;
    private bool registeredRunnerCallbacks;

    private CancellationTokenSource? playActionCancellationSource;
    private CancellationToken playActionCancellation = CancellationToken.None;

    private string playingThrivePopupTitle = string.Empty;
    private string playPopupTopMessage = string.Empty;
    private string playPopupBottomMessage = string.Empty;

    private List<ThriveOutputMessage> thriveOutputFirstPart = new();

    private DevBuildVersion? pendingBuildToPlay;

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
                logger.LogDebug($"{nameof(ThriveIsRunning)} is set to false");
                ShowCloseButtonOnPlayPopup = true;

                // We don't want to block here waiting for this
                _ = CheckLauncherUnHide();
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

    public ObservableCollection<string> PlayMessages { get; } = new();

    public ObservableCollection<FilePrepareProgress> InProgressPlayOperations { get; } = new();

    public List<ThriveOutputMessage> ThriveOutputFirstPart
    {
        get => thriveOutputFirstPart;
        private set => this.RaiseAndSetIfChanged(ref thriveOutputFirstPart, value);
    }

    public bool ThriveOutputIsTruncated
    {
        get => thriveOutputIsTruncated;
        private set => this.RaiseAndSetIfChanged(ref thriveOutputIsTruncated, value);
    }

    public ObservableCollection<ThriveOutputMessage> ThriveOutputLastPart => thriveRunner.ThriveOutputTrailing;

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
        CurrentlyPlaying = true;
        ShowCloseButtonOnPlayPopup = false;

        PlayMessages.Clear();
        InProgressPlayOperations.Clear();

        // Allow canceling the Thrive version setup and download
        CanCancelPlaying = true;

        playActionCancellationSource = new CancellationTokenSource();
        playActionCancellation = playActionCancellationSource.Token;

        if (!registeredInstallerCallbacks)
        {
            RegisterInstallerMessageForwarders();
            registeredInstallerCallbacks = true;
        }

        StartPlayingThrive(version);
    }

    public void ClosePlayingPopup()
    {
        if (playActionCancellationSource == null)
            throw new InvalidOperationException("Playing has not been started");

        if (!playActionCancellationSource.IsCancellationRequested)
        {
            playActionCancellationSource.Cancel();
            logger.LogInformation("User requested cancel of current playing of Thrive");
        }

        logger.LogInformation("Closing play popup due to cancellation");
        CurrentlyPlaying = false;
        CanCancelPlaying = false;
    }

    public void AcceptPlayUnsafeBuild()
    {
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

        // hide the popup
        throw new NotImplementedException();

        logger.LogInformation("Accepted playing potentially unsafe build");
        _ = CheckFilesAndStartThrive(pendingBuildToPlay);
        pendingBuildToPlay = null;
    }

    public void CancelPlayUnsafeBuild()
    {
        CurrentlyPlaying = false;
        pendingBuildToPlay = null;

        // hide the popup
        throw new NotImplementedException();

        ReportRunFailure(Resources.PlayingUnsafeBuildWasCanceled);
    }

    private async void StartPlayingThrive(IPlayableVersion version)
    {
        if (version is DevBuildVersion devBuildVersion)
        {
            // For devbuild type builds we need to start retrieving the build info before we can check if we have it
            // installed
            Dispatcher.UIThread.Post(() =>
            {
                PlayPopupTopMessage = string.Format(Resources.GettingDevBuildInfoMessage,
                    settingsManager.Settings.SelectedDevBuildType.ToString());
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

            if (build.Anonymous && !build.Verified)
            {
                logger.LogInformation("DevBuild is an unsafe build, asking for confirmation first");

                // TODO: implement
                throw new NotImplementedException();
            }
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

            if (!registeredRunnerCallbacks)
            {
                RegisterThriveRunnerListeners();
                registeredRunnerCallbacks = true;
            }

            thriveRunner.LDPreload = launcherOptions.GameLDPreload;

            if (launcherOptions.ThriveExtraFlags is { Count: > 0 })
            {
                thriveRunner.ExtraThriveStartFlags = launcherOptions.ThriveExtraFlags;
            }
            else
            {
                thriveRunner.ExtraThriveStartFlags = null;
            }

            thriveRunner.StartThrive(version, playActionCancellation);

            if (settingsManager.Settings.CloseLauncherOnGameStart)
            {
                _ = CloseLauncherAfterStart();
            }
            else if (settingsManager.Settings.HideLauncherOnPlay)
            {
                _ = HideLauncherAfterStart();
            }
        });
    }

    private void ReportRunFailure(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlayPopupTopMessage = string.Format(Resources.ThriveRunError, message);
            CanCancelPlaying = true;
            ShowCloseButtonOnPlayPopup = true;
        });
    }

    private void RegisterInstallerMessageForwarders()
    {
        thriveInstaller.InstallerMessages.CollectionChanged += (_, _) =>
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
        };

        thriveInstaller.InProgressOperations.CollectionChanged += (_, args) =>
        {
            if (!CurrentlyPlaying)
            {
                logger.LogWarning("Ignoring installer progress message as not currently playing");
                return;
            }

            logger.LogDebug("Installer in progress operation change type: {Action}, new: {NewItems}", args.Action,
                args.NewItems);

            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    InProgressPlayOperations.AddOrInsertRange((IEnumerable<FilePrepareProgress>)args.NewItems!,
                        args.NewStartingIndex);

                    break;
                case NotifyCollectionChangedAction.Remove:
                    for (int i = 0; i < args.OldItems!.Count; ++i)
                    {
                        InProgressPlayOperations.RemoveAt(args.OldStartingIndex);
                    }

                    break;
                case NotifyCollectionChangedAction.Replace:
                    for (int i = 0; i < args.OldItems!.Count; ++i)
                    {
                        InProgressPlayOperations.RemoveAt(args.OldStartingIndex);
                    }

                    goto case NotifyCollectionChangedAction.Add;

                // For now move is not implemented
                // case NotifyCollectionChangedAction.Move:
                //     break;
                case NotifyCollectionChangedAction.Reset:
                    InProgressPlayOperations.Clear();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        };
    }

    private void RegisterThriveRunnerListeners()
    {
        thriveRunner.ThriveRunningObservable.Subscribe(new LambdaBasedObserver<bool>(value =>
        {
            ThriveIsRunning = value;
        }));

        thriveRunner.OutputTruncated.Subscribe(new LambdaBasedObserver<bool>(value =>
        {
            ThriveOutputIsTruncated = value;
        }));

        // The runner's messages overwrite the folder setup messages as both aren't important to show at once
        thriveRunner.PlayMessages.CollectionChanged += (_, _) =>
        {
            if (!CurrentlyPlaying)
            {
                logger.LogWarning("Ignoring runner message as not currently playing");
                return;
            }

            logger.LogDebug("Redoing play messages due to changes to runner messages");

            PlayMessages.Clear();
            PlayMessages.AddRange(thriveRunner.PlayMessages.Select(FormatPlayMessage));
        };

        // Initial bunch of messages is never removed from so we can only append or redo the whole thing
        thriveRunner.ThriveOutput.CollectionChanged += (_, args) =>
        {
            if (!CurrentlyPlaying)
            {
                logger.LogWarning("Ignoring runner output message as not currently playing");
                return;
            }

            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ThriveOutputFirstPart.AddOrInsertRange((IEnumerable<ThriveOutputMessage>)args.NewItems!,
                        args.NewStartingIndex);

                    break;
                case NotifyCollectionChangedAction.Reset:
                    ThriveOutputFirstPart.Clear();
                    break;
            }

            this.RaisePropertyChanged(nameof(ThriveOutputFirstPart));
        };

        // The second part of messages is exposed as the original object as whoever handles that should take
        // the partial updates into account for best performance
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

        // TODO: handle closing
        throw new NotImplementedException();
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

        // TODO:
        throw new NotImplementedException();
    }

    private async Task CheckLauncherUnHide()
    {
        // We have no relevant cancellation token here
        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(LauncherConstants.RestoreDelayAfterGameEnd);

        if (!thriveRunner.ThriveRunning)
        {
            logger.LogInformation("Showing the launcher again after running Thrive");

            // TODO:
            throw new NotImplementedException();
        }
    }
}
