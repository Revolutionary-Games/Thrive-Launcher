namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using DevCenterCommunication.Models;
using Models;

public interface IAutoUpdater
{
    /// <summary>
    ///   Progress reporting for the current update process
    /// </summary>
    public ObservableCollection<FilePrepareProgress> InProgressOperations { get; }

    /// <summary>
    ///   Performs an auto update. <see cref="CheckFailedAutoUpdate"/> should be called before calling this to preserve
    ///   knowledge about existing already downloaded updater files.
    /// </summary>
    /// <param name="installerDownload">The download to download from</param>
    /// <param name="updateChannel">
    ///     The update channel the download is for, needs to be known for how to handle the update file
    /// </param>
    /// <param name="currentVersion">
    ///     The current version of the launcher, this is needed to detect whether the update succeeded or not.
    /// </param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>
    ///   True when the update is successful and the installer for the new version should have started (and this
    ///   instance of the launcher should auto close soon)
    /// </returns>
    public Task<bool> PerformAutoUpdate(DownloadableInfo installerDownload, LauncherAutoUpdateChannel updateChannel,
        string currentVersion, CancellationToken cancellationToken);

    /// <summary>
    ///   Should be called when the launcher is detected as the latest version. This clears the auto-update temporary
    ///   files.
    /// </summary>
    public Task NotifyLatestVersionInstalled();

    /// <summary>
    ///   Returns true when there's auto-update files that haven't been deleted
    /// </summary>
    /// <param name="currentVersion">
    ///   The current version of the running launcher. Used to compare against the version we tried to update from to
    ///   detect if we are now using a different version or not.
    /// </param>
    /// <returns>True when updating has failed and the user should be notified</returns>
    public Task<bool> CheckFailedAutoUpdate(string currentVersion);

    public IEnumerable<string> GetPathsToAlreadyDownloadedUpdateFiles();

    public Task<bool> RetryUpdateApplying(string downloadedUpdateFile, LauncherAutoUpdateChannel updateChannelType,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Clears the updater files even though there's a failure
    /// </summary>
    public Task ClearAutoUpdaterFiles();
}
