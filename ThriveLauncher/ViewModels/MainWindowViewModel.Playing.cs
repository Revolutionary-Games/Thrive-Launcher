namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using DynamicData;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   This partial class handles everything related to the setting up and playing Thrive (and the related popup)
/// </summary>
public partial class MainWindowViewModel
{
    private bool currentlyPlaying;
    private bool canCancelPlaying;
    private bool showCloseButtonOnPlayPopup;

    private bool registeredInstallerCallbacks;

    private CancellationTokenSource? playActionCancellationSource;
    private CancellationToken playActionCancellation = CancellationToken.None;

    private string playingThrivePopupTitle = string.Empty;
    private string playPopupTopMessage = string.Empty;
    private string playPopupBottomMessage = string.Empty;

    public bool CanPressPlayButton => !CurrentlyPlaying && !string.IsNullOrEmpty(SelectedVersionToPlay);

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
        }

        logger.LogInformation("Checking that Thrive version is good to play or starting download for it");
        if (!await thriveInstaller.EnsureVersionIsDownloaded(version, playActionCancellation))
        {
            ReportRunFailure(Resources.VersionInstallationOrCheckFailed);
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

            // TODO: implement playing
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

    private string FormatPlayMessage(ThrivePlayMessage message)
    {
        switch (message.MessageType)
        {
            case ThrivePlayMessage.Type.Downloading:
                return message.Format(Resources.DownloadingItem);
            case ThrivePlayMessage.Type.DownloadingFailed:
                return message.Format(Resources.DownloadError);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
