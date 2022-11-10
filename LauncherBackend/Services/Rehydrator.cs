namespace LauncherBackend.Services;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication;
using DevCenterCommunication.Models;
using DevCenterCommunication.Utilities;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

public class Rehydrator : IRehydrator
{
    private readonly ILogger<Rehydrator> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly ILauncherPaths launcherPaths;
    private readonly IDevCenterClient devCenterClient;
    private readonly INetworkDataRetriever networkDataRetriever;
    private readonly IExternalTools externalTools;

    /// <summary>
    ///   Used to show the downloaded dehydrated objects in a more user friendly way
    /// </summary>
    private readonly Dictionary<string, string> knownFinalFilePathsForDehydrated = new();

    private readonly Stack<HttpClient> availableDownloadClients = new();

    public Rehydrator(ILogger<Rehydrator> logger, ILauncherSettingsManager settingsManager,
        ILauncherPaths launcherPaths,
        IDevCenterClient devCenterClient, INetworkDataRetriever networkDataRetriever, IExternalTools externalTools)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;
        this.launcherPaths = launcherPaths;
        this.devCenterClient = devCenterClient;
        this.networkDataRetriever = networkDataRetriever;
        this.externalTools = externalTools;
    }

    public bool AlwaysShowDownloadHashes { get; set; }
    public bool ShowFullDownloadUrls { get; set; }

    private string DehydratedCacheFolder => settingsManager.Settings.DehydratedCacheFolder ??
        launcherPaths.PathToDefaultDehydrateCacheFolder;

    private string TemporaryFolder => settingsManager.Settings.TemporaryDownloadsFolder ??
        launcherPaths.PathToTemporaryFolder;

    public async Task Rehydrate(string dehydratedCacheFile,
        ObservableCollection<FilePrepareProgress> inProgressOperations, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(dehydratedCacheFile) ??
            throw new ArgumentException("Couldn't get dehydrated folder");

        logger.LogInformation("Starting rehydration in folder {Folder} using info file {DehydratedCacheFile}", folder,
            dehydratedCacheFile);

        Directory.CreateDirectory(DehydratedCacheFolder);

        var operation =
            new FilePrepareProgress(Path.GetFileName(dehydratedCacheFile), FilePrepareStep.Processing);
        inProgressOperations.Add(operation);

        DehydrateCache dehydrated;
        try
        {
            await using var reader = File.OpenRead(dehydratedCacheFile);
            dehydrated = await JsonSerializer.DeserializeAsync<DehydrateCache>(reader,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken) ??
                throw new NullDecodedJsonException();
        }
        catch (Exception e)
        {
            throw new Exception($"Failed to read dehydrate info: {e}", e);
        }

        await ProcessDehydratedFile(folder, dehydrated, operation, inProgressOperations, cancellationToken);

        // Delete the dehydrate info file now that rehydration is complete
        logger.LogDebug("Deleting {DehydratedCacheFile} as rehydration is complete", dehydratedCacheFile);
        File.Delete(dehydratedCacheFile);

        inProgressOperations.Remove(operation);
        knownFinalFilePathsForDehydrated.Clear();
    }

    private async Task ProcessDehydratedFile(string dehydratedFolder, DehydrateCache dehydrated,
        FilePrepareProgress overallProgress, ObservableCollection<FilePrepareProgress> inProgressOperations,
        CancellationToken cancellationToken)
    {
        knownFinalFilePathsForDehydrated.Clear();

        // First count total items
        // And also get a list of things we need to download
        var missingHashes = FindMissingDehydratedObjects(dehydrated, out int total);

        overallProgress.FinishedProgress = total;

        // Download missing
        if (missingHashes.Count > 0)
        {
            logger.LogInformation("Downloading {Count} missing dehydrated objects", missingHashes.Count);

            await DownloadDehydratedObjects(missingHashes, inProgressOperations, cancellationToken);

            logger.LogDebug("Done downloading missing objects");
        }

        int processed = 0;

        // Then process the items now that we should have all of the dehydrated objects we need
        foreach (var (fileName, data) in dehydrated.Files)
        {
            if (data.Type == "pck")
            {
                // Pck files have sub items, this is checked earlier in FindMissingDehydratedObjects but here as well
                // for null checker to be happy
                if (data.Data == null)
                    throw new Exception("Pck type should have sub item data");

                // Run pck tool to repack it
                var operations = new List<PckOperation>();

                foreach (var (pckEntry, pckEntryData) in data.Data.Files)
                {
                    var identifier = pckEntryData.GetDehydratedObjectIdentifier();

                    if (!HaveWeAlreadyDownloadedDehydratedObject(identifier))
                    {
                        throw new Exception(
                            "Dehydrate cache item to be used for a .pck file is missing even though it should " +
                            "have been downloaded");
                    }

                    operations.Add(new PckOperation(PathForDehydratedObject(identifier), pckEntry));
                }

                logger.LogDebug("Running godotpcktool to rehydrate {FileName} with {Count} operations", fileName,
                    operations.Count);
                await externalTools.RunGodotPckTool(Path.Join(dehydratedFolder, fileName), operations,
                    cancellationToken);

                processed += data.Data.Files.Count;
            }
            else
            {
                // Just a single file. Copy it to the target folder
                var targetPath = Path.Join(dehydratedFolder, fileName);

                CopyFromDehydrateCache(data.GetDehydratedObjectIdentifier(), targetPath);

                if (!File.Exists(targetPath))
                    throw new Exception($"Failed to copy file from dehydrate cache to: {targetPath}");

                await PerformSpecialFileActions(targetPath, fileName, cancellationToken);

                ++processed;
            }

            overallProgress.CurrentProgress = processed;
        }
    }

    private HashSet<DehydratedObjectIdentification> FindMissingDehydratedObjects(DehydrateCache dehydrated, out int total)
    {
        total = 0;
        var missingHashes = new HashSet<DehydratedObjectIdentification>();

        foreach (var (fileName, data) in dehydrated.Files)
        {
            if (data.Type == "pck")
            {
                if (data.Data == null)
                    throw new Exception("Pck type should have sub item data");

                // Pck files have sub items
                foreach (var (pckEntry, pckEntryData) in data.Data.Files)
                {
                    var identifier = pckEntryData.GetDehydratedObjectIdentifier();

                    if (!HaveWeAlreadyDownloadedDehydratedObject(identifier))
                        missingHashes.Add(identifier);

                    knownFinalFilePathsForDehydrated[identifier.Sha3] = pckEntry;
                    ++total;
                }
            }
            else
            {
                // Just a single file
                var identifier = data.GetDehydratedObjectIdentifier();

                if (!HaveWeAlreadyDownloadedDehydratedObject(identifier))
                    missingHashes.Add(identifier);

                knownFinalFilePathsForDehydrated[identifier.Sha3] = fileName;
                ++total;
            }
        }

        return missingHashes;
    }

    private string PathForDehydratedObject(DehydratedObjectIdentification dehydratedObject)
    {
        return Path.Join(DehydratedCacheFolder, dehydratedObject.Sha3);
    }

    private bool HaveWeAlreadyDownloadedDehydratedObject(DehydratedObjectIdentification dehydratedObject)
    {
        return File.Exists(PathForDehydratedObject(dehydratedObject));
    }

    private async Task DownloadDehydratedObjects(IReadOnlyCollection<DehydratedObjectIdentification> missingObjects,
        ObservableCollection<FilePrepareProgress> inProgressOperations, CancellationToken cancellationToken)
    {
        var total = missingObjects.Count;
        int processed = 0;

        // This is kind of an overall download operation but using download would show our progress as bytes instead
        // of what we want
        var operation = new FilePrepareProgress("missing dehydrated objects", FilePrepareStep.Processing)
        {
            FinishedProgress = total,
        };
        inProgressOperations.Add(operation);

        var downloadTasks = new List<Task>();

        foreach (var chunk in missingObjects.Chunk(CommunicationConstants.MAX_DEHYDRATED_DOWNLOAD_BATCH))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<DehydratedObjectDownloads.DehydratedObjectDownload> downloads;

            try
            {
                downloads = await devCenterClient.GetDownloadsForDehydrated(chunk) ??
                    throw new Exception("Got empty downloads response");
            }
            catch (Exception e)
            {
                throw new Exception(
                    $"Failed to get downloads for dehydrated objects. Is our DevCenter login still valid? Error: {e}",
                    e);
            }

            void ReportOverallProgress()
            {
                lock (operation)
                {
                    ++processed;
                    operation.CurrentProgress = processed;
                }
            }

            // Setup parallel download tasks
            foreach (var downloadsChunk in downloads.Chunk(
                         (int)Math.Ceiling(downloads.Count /
                             (float)LauncherConstants.SimultaneousRehydrationDownloads)))
            {
                downloadTasks.Add(DownloadChunkOfDehydratedObjects(downloadsChunk, ReportOverallProgress,
                    inProgressOperations, cancellationToken));
            }

            // And wait for them before starting the next batch
            foreach (var downloadTask in downloadTasks)
            {
                await downloadTask;
            }

            downloadTasks.Clear();
        }

        inProgressOperations.Remove(operation);
    }

    private async Task DownloadChunkOfDehydratedObjects(DehydratedObjectDownloads.DehydratedObjectDownload[] downloads,
        Action reportItemDownloaded, ObservableCollection<FilePrepareProgress> inProgressOperations,
        CancellationToken cancellationToken)
    {
        var client = GetDownloadClient();

        foreach (var download in downloads)
        {
            var target = PathForDehydratedObject(download);
            var tempTarget = target + ".tmp";
            var tempZipped = Path.Join(TemporaryFolder, download.Sha3 + ".gz");

            // We show the final path to the user in the progress to make it clearer what's going on
            if (!knownFinalFilePathsForDehydrated.TryGetValue(download.Sha3, out var shownPath))
            {
                shownPath = $"unknown ({download.Sha3})";
            }
            else if (AlwaysShowDownloadHashes)
            {
                shownPath = $"{shownPath} ({download.Sha3})";
            }

            var uriToShow = ShowFullDownloadUrls ?
                download.DownloadUrl.UriWithoutQuery() :
                new Uri(download.DownloadUrl).Host;

            var downloadProgress = new FilePrepareProgress(shownPath, uriToShow, "unknown");
            inProgressOperations.Add(downloadProgress);

            try
            {
                var response = await client.GetAsync(download.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var writer = File.OpenWrite(tempZipped);

                await (await response.Content.ReadAsStreamAsync(cancellationToken)).CopyToAsync(writer,
                    cancellationToken);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to download dehydrated object: {e}", e);
            }

            logger.LogDebug("Downloaded {TempZipped}", tempZipped);

            downloadProgress.MoveToExtractStep();

            // Unzip the downloaded file
            try
            {
                await UnGzip(tempZipped, tempTarget, cancellationToken);
            }
            finally
            {
                File.Delete(tempZipped);
            }

            downloadProgress.MoveToVerifyStep();

            // Verify hash now after unzipping as that's how the hashes for these dehydrated files work
            var hash = FileUtilities.HashToHex(await FileUtilities.CalculateSha3OfFile(tempTarget, cancellationToken));

            if (hash != download.Sha3)
            {
                logger.LogError("Downloaded dehydrated object hash doesn't match what it should be");
                throw new Exception("Downloaded dehydrated object doesn't have the correct hash");
            }

            File.Move(tempTarget, target);

            if (!HaveWeAlreadyDownloadedDehydratedObject(download))
            {
                throw new Exception(
                    "Downloaded and verified dehydrated cache item is missing after move to final location");
            }

            inProgressOperations.Remove(downloadProgress);
            reportItemDownloaded.Invoke();
        }

        ReturnDownloadClient(client);
    }

    private async Task UnGzip(string gzipFile, string writeResultTo, CancellationToken cancellationToken)
    {
        await using var reader = File.OpenRead(gzipFile);
        await using var gzReader = new GZipStream(reader, CompressionMode.Decompress);

        await using var fileWriter = File.OpenWrite(writeResultTo);

        await gzReader.CopyToAsync(fileWriter, cancellationToken);
    }

    private void CopyFromDehydrateCache(DehydratedObjectIdentification dehydratedObject, string fullTargetPath)
    {
        var folder = Path.GetDirectoryName(fullTargetPath);

        logger.LogDebug("Copying {DehydratedObject} from dehydrate cache to {FullTargetPath}", dehydratedObject,
            fullTargetPath);

        if (folder != null)
            Directory.CreateDirectory(folder);

        File.Copy(PathForDehydratedObject(dehydratedObject), fullTargetPath);
    }

    private async Task PerformSpecialFileActions(string fullPath, string relativePath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Workaround for executable bit not being preserved. Sets the execute bit on "Thrive"

            if (relativePath == "Thrive")
            {
                logger.LogInformation("Restoring execute bit on {FullPath}", fullPath);

                var startInfo = new ProcessStartInfo("chmod");
                startInfo.ArgumentList.Add("+x");
                startInfo.ArgumentList.Add(fullPath);

                var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);

                if (result.ExitCode != 0)
                {
                    throw new Exception(
                        "Failed to add execute bit to Thrive executable, running chmod failed (exit code: " +
                        $"{result.ExitCode}), output: {result.FullOutput}");
                }
            }
        }
    }

    private HttpClient GetDownloadClient()
    {
        if (availableDownloadClients.Count > 0)
            return availableDownloadClients.Pop();

        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2),
        };

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(networkDataRetriever.UserAgent);

        return client;
    }

    private void ReturnDownloadClient(HttpClient client)
    {
        if (availableDownloadClients.Contains(client))
            throw new ArgumentException("Client already returned");

        availableDownloadClients.Push(client);
    }
}

/// <summary>
///   Handles rehydrating dehydrated Thrive folders
/// </summary>
public interface IRehydrator
{
    /// <summary>
    ///   Processes a dehydrated JSON info file to rehydrate the folder it is contained in
    /// </summary>
    /// <param name="dehydratedCacheFile">Path to the dehydrated cache</param>
    /// <param name="inProgressOperations">Where to show progress</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <exception cref="Exception">When rehydration fails (or a more derived and specific exception)</exception>
    public Task Rehydrate(string dehydratedCacheFile,
        ObservableCollection<FilePrepareProgress> inProgressOperations, CancellationToken cancellationToken);
}
