namespace LauncherBackend.Services;

using Microsoft.Extensions.Logging;

/// <summary>
///   Automatically cleans old files from the temporary folder
/// </summary>
public class TemporaryFilesCleaner
{
    private readonly ILauncherSettingsManager settingsManager;
    private readonly ILauncherPaths launcherPaths;
    private readonly ILogger<TemporaryFilesCleaner> logger;

    private readonly HashSet<string> filesToDeleteOnShutdown = new();

    private readonly bool enabled;

    public TemporaryFilesCleaner(ILauncherSettingsManager settingsManager, ILauncherPaths launcherPaths,
        ILogger<TemporaryFilesCleaner> logger)
    {
        this.settingsManager = settingsManager;
        this.launcherPaths = launcherPaths;
        this.logger = logger;

        // This doesn't need to be re-read as the old file delete is only done on startup (before the user can change
        // the option)
        enabled = settingsManager.Settings.AutoCleanTemporaryFolder;

        logger.LogDebug("Temporary file cleaning enabled: {Enabled}", enabled);
    }

    public void RegisterBigTemporaryFileForDelete(string file)
    {
        lock (filesToDeleteOnShutdown)
        {
            if (filesToDeleteOnShutdown.Add(file))
            {
                logger.LogDebug("Registered big temporary file to delete: {File}", file);
            }
        }
    }

    public async Task Perform()
    {
        if (!enabled)
            return;

        var task = new Task(() =>
        {
            var folder = settingsManager.Settings.TemporaryDownloadsFolder ?? launcherPaths.PathToTemporaryFolder;

            logger.LogInformation("Cleaning any old temporary files in {Folder}", folder);

            var now = DateTime.Now;
            var deleteOlderThan = now - TimeSpan.FromHours(LauncherConstants.DeleteTempFilesAfterHours);

            try
            {
                if (!Directory.Exists(folder))
                {
                    logger.LogDebug("Skipping temporary clean as folder doesn't exist");
                    return;
                }

                // The ToList call is probably unnecessary but shouldn't matter in terms of used memory etc.
                foreach (var file in Directory.EnumerateFiles(folder).ToList())
                {
                    var time = File.GetLastWriteTime(file);

                    if (time < deleteOlderThan)
                    {
                        logger.LogInformation("Deleting old temporary file: {File}", file);
                        File.Delete(file);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error when trying to clean temporary files");
            }
        });

        try
        {
            task.Start();
            await task;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to run background file clean");
        }
    }

    public void OnShutdown()
    {
        List<string> filesToDelete;

        lock (filesToDeleteOnShutdown)
        {
            if (filesToDeleteOnShutdown.Count < 1)
                return;

            filesToDelete = filesToDeleteOnShutdown.ToList();
            filesToDeleteOnShutdown.Clear();
        }

        logger.LogInformation("Deleting {Count} temporary file(s) that are big immediately", filesToDelete.Count);

        foreach (var file in filesToDelete)
        {
            try
            {
                logger.LogDebug("Attempting to delete big temporary file: {File}", file);

                if (Directory.Exists(file))
                {
                    Directory.Delete(file, true);
                }
                else if (File.Exists(file))
                {
                    File.Delete(file);
                }
                else
                {
                    logger.LogDebug("Temporary file no longer exists: {File}", file);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to delete temporary file: {File}", file);
            }
        }
    }
}
