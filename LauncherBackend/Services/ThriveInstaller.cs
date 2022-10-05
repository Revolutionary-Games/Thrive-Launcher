namespace LauncherBackend.Services;

using Microsoft.Extensions.Logging;
using Models;
using Utilities;

/// <summary>
///   Manages downloading and installing Thrive versions
/// </summary>
public class ThriveInstaller : IThriveInstaller
{
    private readonly ILogger<ThriveInstaller> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly IThriveAndLauncherInfoRetriever infoRetriever;
    private readonly IStoreVersionDetector storeVersionDetector;
    private readonly ILauncherTranslations launcherTranslations;
    private readonly IDevCenterClient devCenterClient;
    private readonly ILauncherPaths launcherPaths;

    public ThriveInstaller(ILogger<ThriveInstaller> logger, ILauncherSettingsManager settingsManager,
        IThriveAndLauncherInfoRetriever infoRetriever, IStoreVersionDetector storeVersionDetector,
        ILauncherTranslations launcherTranslations, IDevCenterClient devCenterClient, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;
        this.infoRetriever = infoRetriever;
        this.storeVersionDetector = storeVersionDetector;
        this.launcherTranslations = launcherTranslations;
        this.devCenterClient = devCenterClient;
        this.launcherPaths = launcherPaths;
    }

    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions()
    {
        var detectedStore = storeVersionDetector.Detect();

        if (detectedStore.IsStoreVersion)
        {
            yield return (string.Format(launcherTranslations.StoreVersionName, detectedStore.StoreName),
                new StoreVersion(detectedStore.StoreName));
        }

        if (devCenterClient.DevCenterConnection != null)
            yield return ("DevBuild", new DevBuildVersion(PlayableDevCenterBuildType.DevBuild));

        // This is null in store versions where we haven't got external versions enabled, or if the version loading
        // failed entirely (but in that case the player should be prevented from interacting with the play button)
        if (infoRetriever.CurrentlyLoadedInfo != null)
        {
            foreach (var version in infoRetriever.CurrentlyLoadedInfo.Versions)
            {
                var allPlatformsForVersion = version.Platforms.Keys.ToList();

                foreach (var versionPlatform in version.Platforms)
                {
                    if (!settingsManager.Settings.ShouldShowVersionWithPlatform(versionPlatform.Key,
                            allPlatformsForVersion))
                        continue;

                    bool latest = infoRetriever.CurrentlyLoadedInfo.IsLatest(version);

                    var name = string.Format(launcherTranslations.VersionWithPlatform, version.ReleaseNumber,
                        versionPlatform.Key.ToString());

                    // Latest version has a custom suffix to identify it easier
                    if (latest)
                        name = string.Format(launcherTranslations.LatestVersionTag, name);

                    yield return (version.ReleaseNumber,
                        new PlayableVersion(name, version, latest, versionPlatform.Value.LocalFileName));
                }
            }
        }
    }

    public IEnumerable<string> DetectInstalledThriveFolders()
    {
        return ListFoldersInThriveInstallFolder().Where(f => f.IsThriveFolder).Select(f => f.Path);
    }

    public IEnumerable<FolderInInstallFolder> ListFoldersInThriveInstallFolder()
    {
        var installFolder = settingsManager.Settings.ThriveInstallationPath ??
            launcherPaths.PathToDefaultThriveInstallFolder;

        if (!Directory.Exists(installFolder))
        {
            logger.LogInformation("Install folder doesn't exist at: {InstallFolder}", installFolder);
            yield break;
        }

        logger.LogDebug("Checking installed versions in {InstallFolder}", installFolder);

        var thriveFolders = new HashSet<string>();

        foreach (var (_, versionObject) in GetAvailableThriveVersions())
        {
            if (versionObject is StoreVersion)
                continue;

            var fullPath = Path.GetFullPath(Path.Join(installFolder, versionObject.FolderName));
            thriveFolders.Add(fullPath);
        }

        foreach (var directory in Directory.EnumerateDirectories(installFolder))
        {
            var full = Path.GetFullPath(directory);

            bool isThriveRelated = thriveFolders.Contains(full);

            var folderInfo = new FolderInInstallFolder(full, isThriveRelated);

            if (isThriveRelated)
            {
                folderInfo.Size = FileUtilities.CalculateFolderSize(full);
            }

            yield return folderInfo;
        }
    }

    public IEnumerable<string> ListFilesInTemporaryFolder()
    {
        var temporaryFolder = settingsManager.Settings.TemporaryDownloadsFolder ??
            launcherPaths.PathToTemporaryFolder;

        if (!Directory.Exists(temporaryFolder))
        {
            logger.LogInformation("Temporary folder doesn't exist at: {TemporaryFolder}", temporaryFolder);
            yield break;
        }

        logger.LogDebug("Listing all files in {TemporaryFolder}", temporaryFolder);

        foreach (var entry in Directory.EnumerateFileSystemEntries(temporaryFolder))
        {
            yield return Path.GetFullPath(entry);
        }
    }

    public IEnumerable<string> ListFilesInDehydrateCache()
    {
        var dehydratedFolder = settingsManager.Settings.DehydratedCacheFolder ??
            launcherPaths.PathToDefaultDehydrateCacheFolder;

        if (!Directory.Exists(dehydratedFolder))
        {
            logger.LogInformation("Dehydrated folder doesn't exist at: {DehydratedFolder}", dehydratedFolder);
            yield break;
        }

        logger.LogDebug("Listing all files in {DehydratedFolder}", dehydratedFolder);

        // TODO: if we ever have subfolders in the dehydrate cache, this needs to be updated
        foreach (var entry in Directory.EnumerateFiles(dehydratedFolder))
        {
            yield return Path.GetFullPath(entry);
        }
    }
}

public interface IThriveInstaller
{
    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions();

    public IEnumerable<string> DetectInstalledThriveFolders();

    public IEnumerable<FolderInInstallFolder> ListFoldersInThriveInstallFolder();

    /// <summary>
    ///   Lists all files in the temporary folder, even non-Thrive related
    /// </summary>
    /// <returns>Enumerable of the files</returns>
    public IEnumerable<string> ListFilesInTemporaryFolder();

    /// <summary>
    ///   Lists all files in the dehydrate cache folder, even non-Thrive related (if such files are put there)
    /// </summary>
    /// <returns>Enumerable of the files</returns>
    public IEnumerable<string> ListFilesInDehydrateCache();
}
