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
using FileUtilities = Utilities.FileUtilities;

/// <summary>
///   Manages downloading and installing Thrive versions
/// </summary>
public class ThriveInstaller : IThriveInstaller
{
    private const int DownloadBufferSize = 65536;

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
            yield return (string.Format(launcherTranslations.StoreVersionName, detectedStore.StoreName),
                new StoreVersion(detectedStore.StoreName));
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

        // TODO: create options to control these
        bool showLatestBeta = false;
        bool showAllBetaVersions = false;

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
                    new PlayableVersion(name, versionPlatform.Value, latest, versionPlatform.Value.LocalFileName));
            }
        }
    }

    public IOrderedEnumerable<(string VersionName, IPlayableVersion VersionObject)> SortVersions(
        IEnumerable<(string VersionName, IPlayableVersion VersionObject)> versions)
    {
        // Store version first
        var sorted = versions.OrderBy(t => t.VersionObject is StoreVersion);

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

            if (Directory.Exists(targetFolder))
            {
                if (!await VerifyRightDevBuildIsInstalled(targetFolder, devBuildCacheName, devBuildVersion,
                        cancellationToken))
                {
                    // Get the version to download here to avoid non-async blocking later
                    logger.LogDebug("Fetching download info already for DevBuild to not block later");
                    await devBuildVersion.DownloadAsync;
                    logger.LogDebug("Done getting download info");
                }
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
            var pickedMirror = playableVersion.Download.Mirrors.FirstOrDefault();

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

    public PackagePlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsLinux())
            return PackagePlatform.Linux;

        if (OperatingSystem.IsWindows())
        {
            if (Environment.Is64BitOperatingSystem)
                return PackagePlatform.Windows;

            return PackagePlatform.Windows32;
        }

        if (OperatingSystem.IsMacOS())
            return PackagePlatform.Mac;

        throw new NotSupportedException("Unknown OS to get current platform for");
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

        Directory.CreateDirectory(BaseTempFolder);
        var finalFile = Path.Join(BaseTempFolder, temporaryFileName);

        if (File.Exists(finalFile))
        {
            logger.LogInformation("Deleting file before re-downloading: {FinalFile}", finalFile);
            File.Delete(finalFile);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var writer = File.OpenWrite(finalFile);

        string hash;
        try
        {
            var response = await downloadHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            // Check that we don't accidentally try to unzip a html response
            if (response.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                var contentType = contentTypeValues.FirstOrDefault();

                if (contentType != null)
                {
                    if (contentType.Contains("text") || contentType.Contains("html"))
                    {
                        throw new Exception(
                            $"Expected non-text (and non-html) response from server, got type: {contentType}");
                    }
                }
            }

            var length = response.Content.Headers.ContentLength;

            long downloadedBytes = 0;
            operationProgress.CurrentProgress = downloadedBytes;

            if (length != null)
            {
                operationProgress.FinishedProgress = length;
            }
            else
            {
                logger.LogWarning("Didn't get Content-Length header so can't show download progress");
            }

            var reader = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[DownloadBufferSize];

            // Pass the data simultaneously to the hasher and the file to speed things up
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                if (read <= 0)
                    break;

                downloadedBytes += read;

                var writeTask = writer.WriteAsync(buffer, 0, read, cancellationToken);

                hasher.TransformBlock(buffer, 0, read, null, 0);
                await writeTask;

                // TODO: do we need some kind of rate limit on the progress updates?
                operationProgress.CurrentProgress = downloadedBytes;
            }

            // Finalize the hasher state
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            hash = SharedBase.Utilities.FileUtilities.HashToHex(hasher.Hash ??
                throw new Exception("Hasher didn't calculate hash"));

            logger.LogDebug("Downloaded {DownloadedBytes} bytes with hash of: {Hash}", downloadedBytes, hash);

            if (length != null && downloadedBytes != length)
            {
                logger.LogWarning(
                    "Downloaded bytes doesn't match the Content-Length header {Length} != {DownloadedBytes}", length,
                    downloadedBytes);
            }
        }
        catch (Exception e)
        {
            InstallerMessages.Add(new ThrivePlayMessage(ThrivePlayMessage.Type.DownloadingFailed, e.Message));

            try
            {
                writer.Close();
                File.Delete(finalFile);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to remove failed to download file");
            }

            return null;
        }
        finally
        {
            try
            {
                writer.Close();
            }
            catch (Exception e2)
            {
                // This catch is here mostly so that in the error case where the file needs to be closed early for
                // deleting, this doesn't cause an issue
                logger.LogWarning(e2, "Failed to close temp file writer");
            }
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

public interface IThriveInstaller
{
    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions();

    /// <summary>
    ///   Sorts versions to be in the order they should be shown to the user
    /// </summary>
    /// <param name="versions">The versions to sort</param>
    /// <returns>Versions in sorted order</returns>
    public IOrderedEnumerable<(string VersionName, IPlayableVersion VersionObject)> SortVersions(
        IEnumerable<(string VersionName, IPlayableVersion VersionObject)> versions);

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

    /// <summary>
    ///   Makes sure the version is installed, if not starts the whole process of downloading it.
    /// </summary>
    /// <param name="playableVersion">The version to check</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Task resulting in true when everything is fine</returns>
    public Task<bool> EnsureVersionIsDownloaded(IPlayableVersion playableVersion, CancellationToken cancellationToken);

    /// <summary>
    ///   Gets the current platform we run on for use with Thrive executable detection
    /// </summary>
    /// <returns>The current platform</returns>
    public PackagePlatform GetCurrentPlatform();

    /// <summary>
    ///   Looks for the bin folder (old releases) or the folder containing the Thrive executable folder
    /// </summary>
    /// <param name="installedThriveFolder">The base folder of the installed version to start looking for</param>
    /// <param name="platform">Which platform this install is for (used to know the executable name)</param>
    /// <param name="fallback">
    ///   If true then the last found folder is returned, even if no bin folder is found. Should always be true on the
    ///   top level call to find the folder.
    /// </param>
    /// <returns>The found folder with the Thrive executable or null if not found</returns>
    public string? FindThriveExecutableFolderInVersion(string installedThriveFolder, PackagePlatform platform,
        bool fallback = true);

    public bool ThriveExecutableExistsInFolder(string folder, PackagePlatform platform);

    /// <summary>
    ///   Messages from the current install process
    /// </summary>
    public ObservableCollection<ThrivePlayMessage> InstallerMessages { get; }

    /// <summary>
    ///   Progress reporting for the current installation process
    /// </summary>
    public ObservableCollection<FilePrepareProgress> InProgressOperations { get; }

    public string BaseInstallFolder { get; }
}
