namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using SHA3.Net;
using SharedBase.Models;
using SharedBase.Utilities;
using Utilities;

/// <summary>
///   Manages downloading and installing Thrive versions
/// </summary>
public sealed class ThriveInstaller : IThriveInstaller, IDisposable
{
    private readonly ILogger<ThriveInstaller> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly IThriveAndLauncherInfoRetriever infoRetriever;
    private readonly IStoreVersionDetector storeVersionDetector;
    private readonly ILauncherTranslations launcherTranslations;
    private readonly IDevCenterClient devCenterClient;
    private readonly ILauncherPaths launcherPaths;
    private readonly IExternalTools externalTools;
    private readonly IRehydrator rehydrator;
    private readonly HttpClient downloadHttpClient;

    public ThriveInstaller(ILogger<ThriveInstaller> logger, ILauncherSettingsManager settingsManager,
        IThriveAndLauncherInfoRetriever infoRetriever, IStoreVersionDetector storeVersionDetector,
        ILauncherTranslations launcherTranslations, IDevCenterClient devCenterClient, ILauncherPaths launcherPaths,
        INetworkDataRetriever networkDataRetriever, IExternalTools externalTools, IRehydrator rehydrator)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;
        this.infoRetriever = infoRetriever;
        this.storeVersionDetector = storeVersionDetector;
        this.launcherTranslations = launcherTranslations;
        this.devCenterClient = devCenterClient;
        this.launcherPaths = launcherPaths;
        this.externalTools = externalTools;
        this.rehydrator = rehydrator;

        downloadHttpClient = new HttpClient();
        downloadHttpClient.Timeout = TimeSpan.FromMinutes(3);
        downloadHttpClient.DefaultRequestHeaders.UserAgent.Clear();
        downloadHttpClient.DefaultRequestHeaders.UserAgent.Add(networkDataRetriever.UserAgent);
    }

    public ObservableCollection<ThrivePlayMessage> InstallerMessages { get; } = new();
    public ObservableCollection<FilePrepareProgress> InProgressOperations { get; } = new();

    public string BaseInstallFolder => !string.IsNullOrEmpty(settingsManager.Settings.ThriveInstallationPath) ?
        settingsManager.Settings.ThriveInstallationPath :
        launcherPaths.PathToDefaultThriveInstallFolder;

    private string BaseTempFolder => !string.IsNullOrEmpty(settingsManager.Settings.TemporaryDownloadsFolder) ?
        settingsManager.Settings.TemporaryDownloadsFolder :
        launcherPaths.PathToTemporaryFolder;

    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions()
    {
        var detectedStore = storeVersionDetector.Detect();

        if (detectedStore.IsStoreVersion)
        {
            // TODO: translations for this and the DevBuild name
            yield return (string.Format(launcherTranslations.StoreVersionName, detectedStore.StoreName),
                detectedStore.CreateStoreVersion());
        }

        if (devCenterClient.DevCenterConnection != null)
            yield return ("DevBuild", new DevBuildVersion(PlayableDevCenterBuildType.DevBuild, GetBuildDownload));

        // This is null in store versions where we haven't got external versions enabled, or if the version loading
        // failed entirely (but in that case the player should be prevented from interacting with the play button)
        if (infoRetriever.CurrentlyLoadedInfo == null)
            yield break;

        // Guard against bad data, as somehow this can get called when we checked for bad data beforehand before
        // assigning the variable which should trigger reading this
        if (infoRetriever.CurrentlyLoadedInfo.Versions == null!)
        {
            logger.LogWarning("Loaded Thrive info is bad, returning no available versions");
            yield break;
        }

        var settings = settingsManager.Settings;
        bool showLatestBeta = settings.ShowLatestBetaVersion;
        bool showAllBetaVersions = settings.ShowAllBetaVersions && settings.ShowLatestBetaVersion;

        foreach (var version in infoRetriever.CurrentlyLoadedInfo.Versions)
        {
            if (!version.Stable)
            {
                if (!showAllBetaVersions &&
                    !(showLatestBeta && version.Id == infoRetriever.CurrentlyLoadedInfo.LatestUnstable))
                {
                    continue;
                }
            }

            var allPlatformsForVersion = version.Platforms.Keys.ToList();

            foreach (var versionPlatform in version.Platforms)
            {
                if (!settings.ShouldShowVersionWithPlatform(versionPlatform.Key,
                        allPlatformsForVersion))
                {
                    continue;
                }

                bool latest = infoRetriever.CurrentlyLoadedInfo.IsLatest(version);

                var name = string.Format(launcherTranslations.VersionWithPlatform, version.ReleaseNumber,
                    versionPlatform.Key.ToString());

                // Latest version has a custom suffix to identify it easier
                if (latest)
                    name = string.Format(launcherTranslations.LatestVersionTag, name);

                yield return (version.ReleaseNumber, new PlayableVersion(name, version.ReleaseNumber,
                    versionPlatform.Value, latest, versionPlatform.Value.LocalFileName,
                    version.SupportsFailedStartupDetection));
            }
        }
    }

    public IOrderedEnumerable<(string VersionName, IPlayableVersion VersionObject)> SortVersions(
        IEnumerable<(string VersionName, IPlayableVersion VersionObject)> versions)
    {
        // Store version first (descending is needed to put it first)
        var sorted = versions.OrderByDescending(t => t.VersionObject is StoreVersion);

        // Then devbuilds (sorted by type)
        sorted = sorted.ThenBy(t =>
        {
            if (t.VersionObject is DevBuildVersion buildVersion)
            {
                return (int)buildVersion.BuildType;
            }

            return int.MaxValue;
        });

        var fallbackVersion = new Version(0, 0, 0, 0);
        var highestVersion = new Version(int.MaxValue, 0, 0, 0);

        // Descending order puts the latest build at the top of the list (when the dropdown opens towards down) to
        // make it nicer to see
        sorted = sorted.ThenByDescending(t =>
        {
            if (t.VersionObject is StoreVersion or DevBuildVersion)
                return highestVersion;

            try
            {
                return new Version(t.VersionName.Split("-", 2)[0]);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Cannot parse version number for sorting: {VersionName}", t.VersionName);
                return fallbackVersion;
            }
        });

        // Then by is used here to sort beta versions to be after the full release equivalents
        sorted = sorted.ThenBy(t => t.VersionName);

        return sorted;
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

    public async Task<bool> EnsureVersionIsDownloaded(IPlayableVersion playableVersion,
        CancellationToken cancellationToken)
    {
        InstallerMessages.Clear();
        InProgressOperations.Clear();

        if (playableVersion.IsStoreVersion)
        {
            logger.LogInformation("Version is the store version, it is assumed to be always installed when available");
            return true;
        }

        var targetFolder = Path.Join(BaseInstallFolder, playableVersion.FolderName);

        var devBuildCacheName = Path.Join(targetFolder, LauncherConstants.DevBuildCacheFileName);

        if (playableVersion is DevBuildVersion devBuildVersion)
        {
            if (devBuildVersion.ExactBuild == null)
                throw new InvalidOperationException("Exact build has not been set on DevBuild");

            bool needsToDownload = false;

            if (Directory.Exists(targetFolder))
            {
                if (!await VerifyRightDevBuildIsInstalled(targetFolder, devBuildCacheName, devBuildVersion,
                        cancellationToken))
                {
                    needsToDownload = true;
                }
            }
            else
            {
                needsToDownload = true;
            }

            if (needsToDownload)
            {
                // Get the version to download here to avoid non-async blocking later (which will cause a total lockup)
                logger.LogDebug("Fetching download info already for DevBuild to not block later");
                await devBuildVersion.DownloadAsync;
                logger.LogDebug("Done getting download info");
            }
        }
        else
        {
            if (playableVersion.IsDevBuild)
                throw new OperationCanceledException("Is marked devbuild but not using the right class");
        }

        if (Directory.Exists(targetFolder))
        {
            logger.LogDebug("Folder exists at {TargetFolder}", targetFolder);
        }
        else
        {
            Directory.CreateDirectory(BaseInstallFolder);

            // We need to download and extract the Thrive version
            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.Downloading,
                playableVersion.FolderName));

            if (playableVersion.Download.Mirrors.Count < 1)
            {
                InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.DownloadingFailed,
                    "No download mirror found"));
                return false;
            }

            // TODO: allow user to select preferred mirrors
            var pickedMirror = playableVersion.Download.Mirrors.First();

            logger.LogDebug("Picked mirror: {Key}", pickedMirror.Key);

            if (!await DownloadAndExtract(pickedMirror.Value, pickedMirror.Key, playableVersion.Download.FileSha3,
                    null, targetFolder, cancellationToken))
            {
                logger.LogError("Failed to download and extract Thrive to {TargetFolder}", targetFolder);
                return false;
            }
        }

        // Write DevBuild info file to know what we have already downloaded
        if (playableVersion is DevBuildVersion devBuildVersion2)
        {
            logger.LogInformation("Writing DevBuild exact version info file at {DevBuildCacheName}", devBuildCacheName);
            await File.WriteAllTextAsync(devBuildCacheName, JsonSerializer.Serialize(devBuildVersion2.ExactBuild),
                cancellationToken);
        }

        // Check if it is dehydrated and rehydrate
        var dehydratedFile = Path.Join(targetFolder, LauncherConstants.DehydratedCacheFileName);

        if (File.Exists(dehydratedFile))
        {
            logger.LogInformation("Build is dehydrated (info file at: {DehydratedFile}), beginning rehydration",
                dehydratedFile);

            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.Rehydrating));

            try
            {
                await rehydrator.Rehydrate(dehydratedFile, InProgressOperations, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Rehydration failed with an exception on {DehydratedFile}", dehydratedFile);
                InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.RehydrationFailed,
                    e.Message));
                return false;
            }

            logger.LogInformation("Rehydration succeeded");
        }

        return true;
    }

    public string? FindThriveExecutableFolderInVersion(string installedThriveFolder, PackagePlatform platform,
        bool fallback)
    {
        // We might already be in the right folder
        if (Directory.Exists(Path.Join(installedThriveFolder, "bin")))
            return Path.Join(installedThriveFolder, "bin");

        string? lastFolder = null;

        foreach (var directory in Directory.EnumerateDirectories(installedThriveFolder))
        {
            // Skip mono folder, packaged asar, and node_modules
            if (directory.Contains("Mono") || directory.Contains(".asar") || directory.Contains("node_modules"))
                continue;

            var bin = FindThriveExecutableFolderInVersion(directory, platform, false);

            if (bin != null)
                return bin;

            lastFolder = directory;
        }

        // Newer releases have the executable in the root so we return the top level folder we found
        if (fallback)
        {
            // And devbuilds directly have a thrive exe in the folder to check
            if (ThriveExecutableExistsInFolder(installedThriveFolder, platform))
            {
                return installedThriveFolder;
            }

            if (platform == PackagePlatform.Mac)
            {
                const string macApp = "Thrive.app/Contents/MacOS";

                return Path.Join(installedThriveFolder, macApp);
            }

            return lastFolder;
        }

        return null;
    }

    public bool ThriveExecutableExistsInFolder(string folder, PackagePlatform platform)
    {
        var fileNameToCheck = Path.Join(folder, ThriveProperties.GetThriveExecutableName(platform));

        if (!File.Exists(fileNameToCheck))
            return false;

        // TODO: once possible add check that the file is actually executable
        // if (platform != PackagePlatform.Windows && platform != PackagePlatform.Windows32)
        // {
        //     var info = new FileInfo(fileNameToCheck);
        // }

        return true;
    }

    public void Dispose()
    {
        downloadHttpClient.Dispose();
    }

    /// <summary>
    ///   Verify that the right DevBuild is downloaded in the folder
    /// </summary>
    private async Task<bool> VerifyRightDevBuildIsInstalled(string targetFolder, string devBuildCacheName,
        DevBuildVersion devBuildVersion, CancellationToken cancellationToken)
    {
        bool delete = false;

        if (File.Exists(devBuildCacheName))
        {
            try
            {
                var existingInstalledInfo =
                    JsonSerializer.Deserialize<DevBuildLauncherDTO>(
                        await File.ReadAllTextAsync(devBuildCacheName, cancellationToken)) ??
                    throw new NullDecodedJsonException();

                if (existingInstalledInfo.Id != devBuildVersion.ExactBuild!.Id)
                {
                    logger.LogInformation(
                        "We have installed DevBuild {Id1} but we want to play {Id2}, " +
                        "deleting existing and downloading the right build",
                        existingInstalledInfo.Id, devBuildVersion.ExactBuild.Id);
                    delete = true;
                }
                else
                {
                    logger.LogInformation("We already have installed the right DevBuild ({Id})",
                        devBuildVersion.ExactBuild.Id);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e,
                    "Failed to detect what DevBuild was already installed, deleting the whole thing");
                delete = true;
            }
        }
        else
        {
            logger.LogWarning("DevBuild folder exists, but doesn't have exact version info cache, deleting");
            Directory.Delete(targetFolder, true);
        }

        if (delete)
        {
            Directory.Delete(targetFolder, true);
            return false;
        }

        return true;
    }

    private async Task<bool> DownloadAndExtract(Uri downloadUrl, string downloadSourceName, string hashToVerify,
        string? filename, string finalFolder, CancellationToken cancellationToken)
    {
        filename ??= Path.GetFileName(downloadUrl.AbsolutePath);
        var operation = new FilePrepareProgress(filename, downloadUrl.WithoutQuery(), downloadSourceName);

        InProgressOperations.Add(operation);

        var tempFile = await DownloadToTempFolder(downloadUrl, filename, operation, hashToVerify, Sha3.Sha3256(),
            cancellationToken);

        if (tempFile == null)
        {
            // Failed to download
            return false;
        }

        operation.MoveToExtractStep();

        logger.LogInformation("Beginning extraction of {TempFile} to {FinalFolder}", tempFile, finalFolder);

        Directory.CreateDirectory(BaseInstallFolder);

        var temporaryFolder = Path.Join(BaseInstallFolder, LauncherConstants.TemporaryExtractedFolderName);

        logger.LogDebug("Temporary extract folder is: {TemporaryFolder}", temporaryFolder);

        if (Directory.Exists(temporaryFolder))
            Directory.Delete(temporaryFolder, true);

        Directory.CreateDirectory(temporaryFolder);

        try
        {
            await externalTools.Run7Zip(tempFile, temporaryFolder, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to run 7-zip");

            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.ExtractionFailed,
                $"Extraction tool (7-zip) failed to run due to: {e}"));

            return false;
        }
        finally
        {
            // TODO: should we leave the temporary file alone in case the user wants to retry the download?
            // For now the launcher 2.0 just deletes failed files (and completed files) to keep disk use down
            logger.LogInformation("Deleting temporary file {TempFile}", tempFile);
            File.Delete(tempFile);
        }

        // Remove one level of folder nesting as a nicer thing compared to the old launcher
        MoveExtractedFolderToTarget(temporaryFolder, finalFolder);

        InProgressOperations.Remove(operation);
        return true;
    }

    private void MoveExtractedFolderToTarget(string temporaryExtractFolder, string targetFolder)
    {
        if (Directory.Exists(targetFolder))
        {
            logger.LogInformation("Removing already existing target folder: {TargetFolder}", targetFolder);
            Directory.Delete(targetFolder, true);
        }

        string? singleFolderContent = null;

        foreach (var fileSystemEntry in Directory.EnumerateFileSystemEntries(temporaryExtractFolder))
        {
            if (singleFolderContent == null)
            {
                singleFolderContent = fileSystemEntry;
                continue;
            }

            // Multiple entries
            logger.LogDebug("Folder has multiple directory entries, can't save one level of nesting");
            singleFolderContent = null;
            break;
        }

        if (singleFolderContent != null)
        {
            logger.LogDebug("Saving one level of nesting");

            logger.LogInformation("Moving {SingleFolderContent} -> {TargetFolder}", singleFolderContent, targetFolder);
            Directory.Move(singleFolderContent, targetFolder);
        }
        else
        {
            logger.LogInformation("Moving {TemporaryExtractFolder} -> {TargetFolder}", temporaryExtractFolder,
                targetFolder);
            Directory.Move(temporaryExtractFolder, targetFolder);
        }

        if (Directory.Exists(temporaryExtractFolder))
            Directory.Delete(temporaryExtractFolder, true);
    }

    private async Task<string?> DownloadToTempFolder(Uri downloadUrl, string temporaryFileName,
        FilePrepareProgress operationProgress, string hashToVerify, HashAlgorithm hasher,
        CancellationToken cancellationToken)
    {
        // TODO: allow retrying partial downloads with a byte offset, when retrying we should make sure the
        // downloadUrl matches what it was before (because for example all DevBuilds use the same filename so
        // interrupted DevBuild download when a different build was selected could cause issues)

        var finalFile = Path.Join(BaseTempFolder, temporaryFileName);

        string hash;
        try
        {
            hash = await HashedFileDownloader.DownloadAndHashFile(downloadHttpClient, downloadUrl, finalFile, hasher,
                operationProgress, logger, cancellationToken);
        }
        catch (Exception e)
        {
            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.DownloadingFailed, e.Message));

            return null;
        }

        if (hash != hashToVerify)
        {
            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.DownloadingFailed,
                "File hash mismatch. Was the download interrupted or corrupted?"));

            try
            {
                File.Delete(finalFile);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to remove file with mismatching hash");
            }

            return null;
        }

        logger.LogInformation("Downloaded {FinalFile}", finalFile);
        return finalFile;
    }

    private async Task<DownloadableInfo> GetBuildDownload(DevBuildLauncherDTO build)
    {
        var rawResult = await devCenterClient.GetDownloadForBuild(build);

        if (rawResult == null)
            throw new Exception($"Could not get download for specified DevBuild ({build.Id})");

        logger.LogDebug("Got build download info for {Id}", build.Id);

        return new DownloadableInfo(rawResult.DownloadHash, LauncherConstants.DevBuildFileName,
            new Dictionary<string, Uri>
            {
                { "devcenter", new Uri(rawResult.DownloadUrl) },
            });
    }
}
