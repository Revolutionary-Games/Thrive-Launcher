namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using SHA3.Net;
using SharedBase.Utilities;
using Utilities;

public class AutoUpdater : IAutoUpdater
{
    private readonly ILogger<AutoUpdater> logger;
    private readonly ILauncherPaths launcherPaths;
    private readonly INetworkDataRetriever networkDataRetriever;
    private readonly ILauncherSettingsManager settingsManager;

    private AutoUpdateAttemptInfo? previousAttemptInfo;

    public AutoUpdater(ILogger<AutoUpdater> logger, ILauncherPaths launcherPaths,
        INetworkDataRetriever networkDataRetriever, ILauncherSettingsManager settingsManager)
    {
        this.logger = logger;
        this.launcherPaths = launcherPaths;
        this.networkDataRetriever = networkDataRetriever;
        this.settingsManager = settingsManager;
    }

    public ObservableCollection<FilePrepareProgress> InProgressOperations { get; } = new();

    private string BaseTempFolder => !string.IsNullOrEmpty(settingsManager.Settings.TemporaryDownloadsFolder) ?
        settingsManager.Settings.TemporaryDownloadsFolder :
        launcherPaths.PathToTemporaryFolder;

    public async Task<bool> PerformAutoUpdate(DownloadableInfo installerDownload,
        LauncherAutoUpdateChannel updateChannel, string currentVersion, CancellationToken cancellationToken)
    {
        logger.LogInformation("Beginning auto-update from launcher version {CurrentVersion}", currentVersion);
        InProgressOperations.Clear();

        previousAttemptInfo ??= new AutoUpdateAttemptInfo(currentVersion);

        if (previousAttemptInfo.PreviousLauncherVersion != currentVersion)
        {
            logger.LogInformation("Updating previously attempted auto-update from launcher version");
            previousAttemptInfo.PreviousLauncherVersion = currentVersion;
        }

        if (installerDownload.Mirrors.Count < 1)
        {
            logger.LogError("Launcher update download has no mirrors");
            return false;
        }

        // TODO: preferred mirrors
        var pickedMirror = installerDownload.Mirrors.First();

        logger.LogDebug("Picked mirror: {Key}", pickedMirror.Key);

        var downloadUrl = pickedMirror.Value;

        // TODO: could make it so that the server includes the launcher versions in the update file names, but it's
        // maybe not necessary for now
        var installerFile = Path.Join(BaseTempFolder, installerDownload.LocalFileName);

        // Register the path to be deleted when updating is finished
        previousAttemptInfo.UpdateFiles.Add(installerFile);
        await WritePreviousAttemptInfo();

        logger.LogInformation("Beginning download of installer from {DownloadUrl} to {InstallerFile}", downloadUrl,
            installerFile);

        var filename = Path.GetFileName(downloadUrl.AbsolutePath);
        var operation = new FilePrepareProgress(filename, downloadUrl.WithoutQuery(), pickedMirror.Key);

        InProgressOperations.Add(operation);

        using var downloadHttpClient = new HttpClient();
        downloadHttpClient.Timeout = TimeSpan.FromMinutes(3);
        downloadHttpClient.DefaultRequestHeaders.UserAgent.Clear();
        downloadHttpClient.DefaultRequestHeaders.UserAgent.Add(networkDataRetriever.UserAgent);

        string hash;
        try
        {
            hash = await HashedFileDownloader.DownloadAndHashFile(downloadHttpClient, downloadUrl, installerFile,
                Sha3.Sha3256(), operation, logger, cancellationToken);
        }
        catch (Exception e)
        {
            // TODO: do we want to output more in-depth error messages to the user?
            // InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.DownloadingFailed, e.Message));
            logger.LogError(e, "Failed to download update installer");
            return false;
        }

        operation.MoveToVerifyStep();

        if (hash != installerDownload.FileSha3)
        {
            try
            {
                File.Delete(installerFile);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to remove file with mismatching hash");
            }

            logger.LogError("Downloaded update installer has wrong hash");
            return false;
        }

        operation.MoveToProcessingStep();

        try
        {
            await StartUpdater(installerFile, updateChannel);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to start updater");
            return false;
        }

        logger.LogInformation("Updater should have started");

        InProgressOperations.Remove(operation);
        return true;
    }

    public async Task NotifyLatestVersionInstalled()
    {
        await LoadPreviousAttemptInfo();
        await ClearAutoUpdaterFiles();
    }

    public async Task<bool> CheckFailedAutoUpdate(string currentVersion)
    {
        await LoadPreviousAttemptInfo();

        if (previousAttemptInfo == null)
        {
            return false;
        }

        logger.LogInformation("Detected previous update attempt with old launcher version: {PreviousLauncherVersion}",
            previousAttemptInfo.PreviousLauncherVersion);
        return previousAttemptInfo.PreviousLauncherVersion == currentVersion;
    }

    public IEnumerable<string> GetPathsToAlreadyDownloadedUpdateFiles()
    {
        if (previousAttemptInfo == null)
            yield break;

        foreach (var updateFile in previousAttemptInfo.UpdateFiles)
        {
            if (!File.Exists(updateFile))
                continue;

            yield return updateFile;
        }
    }

    public async Task<bool> RetryUpdateApplying(string downloadedUpdateFile,
        LauncherAutoUpdateChannel updateChannelType, CancellationToken cancellationToken)
    {
        try
        {
            await StartUpdater(downloadedUpdateFile, updateChannelType);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to retry updater running: {DownloadedUpdateFile}", downloadedUpdateFile);
            return false;
        }
    }

    public async Task ClearAutoUpdaterFiles()
    {
        if (previousAttemptInfo == null)
        {
            logger.LogDebug("Nothing to do about auto-update files");
            return;
        }

        foreach (var updateFile in previousAttemptInfo.UpdateFiles)
        {
            if (!File.Exists(updateFile))
            {
                logger.LogInformation("Auto-updater file is already gone: {UpdateFile}", updateFile);
                continue;
            }

            logger.LogInformation("Attempting to delete auto-updater file which is no longer needed: {UpdateFile}",
                updateFile);

            try
            {
                File.Delete(updateFile);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete a file left over from the auto-update process");
                return;
            }
        }

        logger.LogInformation("Clearing auto-update file data");
        previousAttemptInfo = null;
        await WritePreviousAttemptInfo();
    }

    private async Task LoadPreviousAttemptInfo()
    {
        var file = launcherPaths.PathToAutoUpdateFile;

        previousAttemptInfo = null;

        if (!File.Exists(file))
            return;

        try
        {
            await using var reader = File.OpenRead(file);

            previousAttemptInfo = await JsonSerializer.DeserializeAsync<AutoUpdateAttemptInfo>(reader) ??
                throw new NullDecodedJsonException();

            logger.LogInformation("Loaded existing auto-update data file");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load auto-update data file");
        }
    }

    private async Task WritePreviousAttemptInfo()
    {
        var file = launcherPaths.PathToAutoUpdateFile;

        try
        {
            if (previousAttemptInfo == null)
            {
                if (File.Exists(file))
                {
                    logger.LogInformation(
                        "Deleting existing auto-update data file ({File}) as it exists and our data is null", file);
                    File.Delete(file);
                }

                return;
            }

            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(previousAttemptInfo));

            logger.LogInformation("Wrote auto-update data file at {File}", file);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to write auto-update data file");
        }
    }

    private async Task StartUpdater(string installerFile, LauncherAutoUpdateChannel updateChannelType)
    {
        if (!File.Exists(installerFile))
            throw new ArgumentException($"The installer file doesn't exist at: {installerFile}");

        switch (updateChannelType)
        {
            case LauncherAutoUpdateChannel.WindowsInstaller:
            {
                // For Windows we start running the installer through explorer.exe so that it's not our child process
                var explorer = ExecutableFinder.Which("explorer.exe");

                Process? process;
                if (explorer == null)
                {
                    logger.LogError("Could not find explorer.exe, cannot start process as not our child");
                    process = Process.Start(installerFile);
                }
                else
                {
                    var startInfo = new ProcessStartInfo(explorer);
                    startInfo.ArgumentList.Add(installerFile);

                    logger.LogInformation("Launching updater through {Explorer}: {InstallerFile}", explorer,
                        installerFile);

                    process = Process.Start(startInfo);
                }

                if (process == null)
                    logger.LogWarning("Started updater process is null, updater may not have started");

                await Task.Delay(TimeSpan.FromMilliseconds(350));

                if (process is { HasExited: true })
                {
                    logger.LogInformation("Updater process already exited with code: {ExitCode}", process.ExitCode);
                }

                break;
            }

            case LauncherAutoUpdateChannel.MacDmg:
            {
                // For mac we just want to get the .dmg file opened and mounted
                var startInfo = new ProcessStartInfo("open");
                startInfo.ArgumentList.Add(installerFile);

                var process = Process.Start(startInfo);

                if (process == null)
                    logger.LogInformation("Started updater process is null");

                await Task.Delay(TimeSpan.FromMilliseconds(350));

                if (process is { HasExited: true })
                {
                    logger.LogInformation("File opener process for updater already exited with code: {ExitCode}",
                        process.ExitCode);
                }

                break;
            }

            case LauncherAutoUpdateChannel.LinuxUnpacked:
                // This might get implemented at some point in the future
                throw new NotImplementedException();
            default:
                throw new ArgumentOutOfRangeException(nameof(updateChannelType), updateChannelType, null);
        }
    }
}
