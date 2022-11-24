namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   Auto-update part of the view model functionality
/// </summary>
public partial class MainWindowViewModel
{
    private bool registeredUpdaterCallbacks;

    private bool launcherIsLatestVersion;
    private string launcherOutdatedVersionMessage = string.Empty;
    private string launcherOutdatedAdditionalInfo = string.Empty;

    private string autoUpdateVersionDescription = string.Empty;
    private bool autoUpdatingFailed;
    private bool autoUpdatingSucceeded;
    private string autoUpdateFailureExtraMessage = string.Empty;
    private IReadOnlyCollection<string> availableUpdaterFiles = new string[] { };

    private bool showAutoUpdaterPopup;
    private int? autoUpdateClose;

    private CancellationTokenSource autoUpdateCancellation = new();
    private CancellationToken autoUpdateCancellationToken = CancellationToken.None;

    public bool LauncherIsLatestVersion
    {
        get => launcherIsLatestVersion;
        private set => this.RaiseAndSetIfChanged(ref launcherIsLatestVersion, value);
    }

    public string LauncherOutdatedVersionMessage
    {
        get => launcherOutdatedVersionMessage;
        private set => this.RaiseAndSetIfChanged(ref launcherOutdatedVersionMessage, value);
    }

    public string LauncherOutdatedAdditionalInfo
    {
        get => launcherOutdatedAdditionalInfo;
        private set => this.RaiseAndSetIfChanged(ref launcherOutdatedAdditionalInfo, value);
    }

    public string AutoUpdateVersionDescription
    {
        get => autoUpdateVersionDescription;
        private set => this.RaiseAndSetIfChanged(ref autoUpdateVersionDescription, value);
    }

    public bool AutoUpdatingSucceeded
    {
        get => autoUpdatingSucceeded;
        private set => this.RaiseAndSetIfChanged(ref autoUpdatingSucceeded, value);
    }

    public bool ShowAutoUpdaterPopup
    {
        get => showAutoUpdaterPopup;
        private set => this.RaiseAndSetIfChanged(ref showAutoUpdaterPopup, value);
    }

    public bool AutoUpdatingFailed
    {
        get => autoUpdatingFailed;
        private set
        {
            if (value == autoUpdatingFailed)
                return;

            this.RaiseAndSetIfChanged(ref autoUpdatingFailed, value);

            if (autoUpdatingFailed)
            {
                Task.Run(RefreshExistingUpdaterFiles);
            }
        }
    }

    public string AutoUpdateFailureExtraMessage
    {
        get => autoUpdateFailureExtraMessage;
        private set => this.RaiseAndSetIfChanged(ref autoUpdateFailureExtraMessage, value);
    }

    public IReadOnlyCollection<string> AvailableUpdaterFiles
    {
        get => availableUpdaterFiles;
        private set
        {
            this.RaiseAndSetIfChanged(ref availableUpdaterFiles, value);
            this.RaisePropertyChanged(nameof(HasExistingAutoUpdaters));
        }
    }

    public bool HasExistingAutoUpdaters => AvailableUpdaterFiles.Count > 0;

    /// <summary>
    ///   A delay in seconds when the launcher will request to be closed (so that the started updater can run)
    /// </summary>
    public int? AutoUpdateClose
    {
        get => autoUpdateClose;
        set
        {
            if (value == autoUpdateClose)
                return;

            this.RaiseAndSetIfChanged(ref autoUpdateClose, value);
            this.RaisePropertyChanged(nameof(AutoUpdateCloseDelayText));
        }
    }

    public string? AutoUpdateCloseDelayText
    {
        get
        {
            if (autoUpdateClose == null)
                return null;

            if (autoUpdateClose == 1)
                return Resources.SecondsDisplaySingular;

            return string.Format(Resources.SecondsDisplayPlural, autoUpdateClose);
        }
    }

    public ObservableCollection<FilePrepareProgress> InProgressAutoUpdateOperations { get; } = new();

    public string LauncherVersion => versionUtilities.LauncherVersion + LauncherConstants.ModeSuffix;

    public bool CanAutoUpdate => GetUsedAutoUpdateChannel() != null;

    public void CancelAutoUpdate()
    {
        if (autoUpdateCancellation.IsCancellationRequested)
        {
            logger.LogInformation("Canceling auto-update");
            autoUpdateCancellation.Cancel();
        }

        ShowAutoUpdaterPopup = false;
        AutoUpdatingFailed = false;
    }

    public void CloseLauncherAfterAutoUpdateStart()
    {
        logger.LogInformation("User is requesting the launcher closes early after starting auto-update");
        LauncherShouldClose = true;
    }

    public void RetryAutoUpdate(string file)
    {
        // It's probably fine that the user can click on a retry button as many times as they want

        if (!File.Exists(file))
        {
            logger.LogError("Selected auto-update file doesn't exist");
            return;
        }

        var channel = GetUsedAutoUpdateChannel();

        if (channel == null)
        {
            ShowNotice(Resources.AutoUpdateError, Resources.AutoUpdateMissingChannel);
            return;
        }

        logger.LogInformation("Retrying auto-update with updater file: {File}", file);

        // ReSharper disable once MethodSupportsCancellation
        Task.Run(() => RunAutoUpdateRetry(file, channel.Value));
    }

    public void OpenFirstAutoUpdaterFolder()
    {
        var file = autoUpdater.GetPathsToAlreadyDownloadedUpdateFiles().FirstOrDefault();

        if (file == null)
        {
            logger.LogWarning("No auto-updater files exist for opening");
            return;
        }

        OpenAutoUpdaterFolder(file);
    }

    public void OpenAutoUpdaterFolder(string file)
    {
        logger.LogInformation("Trying to open folder of the auto updater file: {File}", file);
        var folder = Path.GetDirectoryName(file);

        if (folder == null)
        {
            logger.LogError("Failed to get folder containing the updater file");
            return;
        }

        FileUtilities.OpenFolderInPlatformSpecificViewer(folder);
    }

    public void ClearFailedAutoUpdates()
    {
        logger.LogInformation("Ignoring and clearing failed auto-updates");
        CancelAutoUpdate();

        // ReSharper disable once MethodSupportsCancellation
        Task.Run(autoUpdater.ClearAutoUpdaterFiles);
    }

    public void CancelCloseAfterAutoUpdate()
    {
        AutoUpdateClose = null;
    }

    private void CheckLauncherVersion(LauncherThriveInformation launcherInfo)
    {
        LauncherOutdatedVersionMessage = string.Empty;
        LauncherOutdatedAdditionalInfo = string.Empty;

        var current = versionUtilities.AssemblyVersion;

        if (!Version.TryParse(launcherInfo.LauncherVersion.LatestVersion, out var latest))
        {
            logger.LogError("Cannot check if we are the latest launcher version due to error");

            // TODO: maybe this should show the general error popup
            return;
        }

        if (latest.Revision == -1)
        {
            // Covert to the same format as assembly version for better comparisons
            latest = new Version(latest.Major, latest.Minor, latest.Build, 0);
        }

        if (current.Equals(latest))
        {
            logger.LogInformation("We are using the latest launcher version: {Latest}", latest);

            // Only show the text that the launcher is up to date if we can really guarantee it by
            // having loaded fresh data
            if (versionInfoIsFresh)
                LauncherIsLatestVersion = true;
        }
        else if (current > latest)
        {
            logger.LogInformation("We are using a newer launcher than is available {Current} > {Latest}", current,
                latest);
        }
        else if (current < latest)
        {
            logger.LogInformation("We are not the latest launcher version, {Current} < {Latest}", current, latest);

            LauncherIsLatestVersion = false;

            // ReSharper disable once MethodSupportsCancellation
            Task.Run(() => OnOutdatedLauncherDetected(launcherInfo.LauncherVersion, current.ToString()));
            return;
        }

        // Clear auto-update data on successfully being an up to date version
        // ReSharper disable once MethodSupportsCancellation
        Task.Run(autoUpdater.NotifyLatestVersionInstalled);
    }

    private async Task OnOutdatedLauncherDetected(LauncherVersionInfo launcherVersion, string currentVersion)
    {
        // Check if auto-updating has been attempted already, but has failed
        if (await autoUpdater.CheckFailedAutoUpdate(currentVersion))
        {
            logger.LogWarning("A failed auto-update has been detected");

            Dispatcher.UIThread.Post(() => { AutoUpdatingFailed = true; });

            return;
        }

        Dispatcher.UIThread.Post(() => TryToSTartAutoUpdate(launcherVersion, currentVersion));
    }

    private void TryToSTartAutoUpdate(LauncherVersionInfo launcherVersion, string currentVersion)
    {
        RegisterAutoUpdaterCallbacks();
        InProgressAutoUpdateOperations.Clear();

        var updateChannel = GetUsedAutoUpdateChannel();

        logger.LogInformation("Launcher update channel is: {UpdateChannel}", updateChannel);

        if (updateChannel != null)
        {
            if (AllowAutoUpdate && !launcherOptions.SkipAutoUpdate)
            {
                logger.LogDebug("Looking for auto-update file for this platform: {UpdateChannel}", updateChannel);

                if (launcherVersion.AutoUpdateDownloads.TryGetValue(updateChannel.Value, out var download))
                {
                    if (download.Mirrors.Count < 1)
                    {
                        logger.LogWarning(
                            "We found an auto update channel object to use, but it doesn't have any download mirrors");
                        LauncherOutdatedAdditionalInfo = Resources.OutdatedLauncherTypeHasNoUpdateFile;
                    }
                    else
                    {
                        logger.LogInformation("Auto-update is possible and should start soon");

                        // ReSharper disable once MethodSupportsCancellation
                        Task.Run(() => RunAutoUpdate(download, updateChannel.Value, currentVersion,
                            launcherVersion.LatestVersion));
                        return;
                    }
                }
                else
                {
                    logger.LogWarning("No auto-update channel found with type: {UpdateChannel}", updateChannel);
                    LauncherOutdatedAdditionalInfo = Resources.OutdatedLauncherTypeHasNoUpdateFile;
                }
            }
            else
            {
                // User settings or command line flags are preventing things
                LauncherOutdatedAdditionalInfo = Resources.OutdatedLauncherUpdateDisabledByUser;
            }
        }
        else
        {
            // Update channel being null means that this type of launcher doesn't know how to update itself
            LauncherOutdatedAdditionalInfo = Resources.OutdatedLauncherTypeCannotUpdate;
        }

        logger.LogInformation("Auto-update could not start");

        bool showOutdatedNotice = true;

        if (launcherVersion.LatestVersionPublishedAt == null)
            logger.LogWarning("The latest launcher version published date is unknown");

        // Show notice only with a delay (if we are that type of build)
#if LAUNCHER_DELAYED_UPDATE_NOTICE
        if (string.IsNullOrEmpty(LauncherOutdatedAdditionalInfo))
            LauncherOutdatedAdditionalInfo = Resources.OutdatedLauncherUpdateHasBeenAvailable;

        var elapsed = DateTime.UtcNow - launcherVersion.LatestVersionPublishedAt;

        if (elapsed == null || elapsed < LauncherConstants.LauncherNotUpdatingWarningThreshold)
        {
            logger.LogInformation("Not time yet to show the outdated notice (elapsed: {Elapsed})", elapsed);
            showOutdatedNotice = false;
        }
#elif LAUNCHER_NO_OUTDATED_NOTICE
        logger.LogInformation(
            "This launcher type is not configured for auto-updates and not configured to show outdated notice");
        showOutdatedNotice = false;
#endif

        if (showOutdatedNotice)
        {
            // Can't trigger auto-update so show outdated heads up, and we aren't blocked from showing the notice
            logger.LogInformation("Auto update not started, showing user we are outdated");

            LauncherOutdatedVersionMessage = string.Format(Resources.OutdatedLauncherVersionComparison,
                LauncherVersion, launcherVersion.LatestVersion);
        }
        else
        {
            logger.LogInformation("We are outdated, but won't show the update notice");
        }
    }

    private async Task RunAutoUpdate(DownloadableInfo download, LauncherAutoUpdateChannel updateChannel,
        string currentVersion, string newVersion)
    {
        logger.LogInformation("Starting auto-update using download {LocalFileName}", download.LocalFileName);

        Dispatcher.UIThread.Post(() =>
        {
            autoUpdateCancellation = new CancellationTokenSource();
            autoUpdateCancellationToken = autoUpdateCancellation.Token;
            ShowAutoUpdaterPopup = true;
            AutoUpdatingFailed = false;
            AutoUpdatingSucceeded = false;
            AutoUpdateVersionDescription =
                string.Format(Resources.LauncherVersionUpdateDescription, newVersion, currentVersion);
        });

        if (!await autoUpdater.PerformAutoUpdate(download, updateChannel, currentVersion,
                autoUpdateCancellationToken))
        {
            logger.LogError("Auto-updating has failed");

            Dispatcher.UIThread.Post(() =>
            {
                AutoUpdatingFailed = true;
                ShowAutoUpdaterPopup = false;
            });
        }
        else
        {
            logger.LogInformation("Auto-updating has been successful, launcher will auto close");

            Dispatcher.UIThread.Post(() => { AutoUpdatingSucceeded = true; });

            // The method manually checks the cancellation
            // ReSharper disable once MethodSupportsCancellation
            await Task.Run(CloseWithDelay);
        }
    }

    private async Task RunAutoUpdateRetry(string updaterFile, LauncherAutoUpdateChannel launcherAutoUpdateChannel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            autoUpdateCancellation = new CancellationTokenSource();
            autoUpdateCancellationToken = autoUpdateCancellation.Token;
            ShowAutoUpdaterPopup = false;
            AutoUpdatingFailed = true;
            AutoUpdatingSucceeded = false;
            AutoUpdateFailureExtraMessage = Resources.RetryingAutoUpdate;
        });

        if (!await autoUpdater.RetryUpdateApplying(updaterFile, launcherAutoUpdateChannel, autoUpdateCancellationToken))
        {
            logger.LogError("Auto-update retry has failed");

            Dispatcher.UIThread.Post(() =>
            {
                AutoUpdatingFailed = true;
                AutoUpdateFailureExtraMessage = Resources.AutoUpdateRetryFailed;
            });
        }
        else
        {
            logger.LogInformation("Auto-update retrying has been successful, launcher will auto close");

            Dispatcher.UIThread.Post(() =>
            {
                AutoUpdatingSucceeded = true;
                ShowAutoUpdaterPopup = true;
                AutoUpdatingFailed = false;
                AutoUpdateFailureExtraMessage = string.Empty;
            });

            // The method manually checks the cancellation
            // ReSharper disable once MethodSupportsCancellation
            await Task.Run(CloseWithDelay);
        }
    }

    private LauncherAutoUpdateChannel? GetUsedAutoUpdateChannel()
    {
#if LAUNCHER_UPDATER_WINDOWS
        return LauncherAutoUpdateChannel.WindowsInstaller;
#elif LAUNCHER_UPDATER_MAC
        return LauncherAutoUpdateChannel.MacDmg;

        // Linux unpacked updating is not implemented
#else
        logger.LogDebug("This launcher type has no configured update channel");
        return null;
#endif
    }

    private void RegisterAutoUpdaterCallbacks()
    {
        if (registeredUpdaterCallbacks)
            return;

        registeredUpdaterCallbacks = true;

        autoUpdater.InProgressOperations.CollectionChanged += OnUpdaterOperationsChanged;
    }

    private void UnRegisterAutoUpdaterCallbacks()
    {
        if (!registeredUpdaterCallbacks)
            return;

        registeredUpdaterCallbacks = false;

        autoUpdater.InProgressOperations.CollectionChanged -= OnUpdaterOperationsChanged;
    }

    private void OnUpdaterOperationsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // We don't ignore updates here as the updater popup opening is a bit complicated logic-wise, so we let the
        // updates always through to ensure the data there is up to date when it is shown

        InProgressAutoUpdateOperations.ApplyChangeFromAnotherCollection(args);
    }

    private async Task CloseWithDelay()
    {
        Dispatcher.UIThread.Post(() => { AutoUpdateClose = 10; });

        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), autoUpdateCancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Canceled waiting for auto updater close delay");
                break;
            }

            var delay = AutoUpdateClose;

            if (delay == null || autoUpdateCancellation.IsCancellationRequested)
            {
                logger.LogInformation("Auto-close after updater start has been canceled");
                break;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (delay.Value <= 0)
                {
                    logger.LogInformation("Auto-close after update start time has elapsed, closing");
                    LauncherShouldClose = true;
                }

                AutoUpdateClose = delay.Value - 1;
            });
        }
    }

    private Task RefreshExistingUpdaterFiles()
    {
        // Reverse is used here to hopefully get the latest files first
        var data = autoUpdater.GetPathsToAlreadyDownloadedUpdateFiles().Reverse().ToList();

        return Dispatcher.UIThread.InvokeAsync(() => { AvailableUpdaterFiles = data; });
    }
}
