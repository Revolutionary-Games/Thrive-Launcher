namespace LauncherBackend.Services;

using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;

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

            yield return new FolderInInstallFolder(full, thriveFolders.Contains(full));
        }
    }
}

public interface IThriveInstaller
{
    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions();

    public IEnumerable<string> DetectInstalledThriveFolders();

    public IEnumerable<FolderInInstallFolder> ListFoldersInThriveInstallFolder();
}
