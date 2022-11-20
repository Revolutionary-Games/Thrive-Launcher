namespace LauncherBackend.Services;

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DevCenterCommunication.Models;
using DevCenterCommunication.Utilities;
using Microsoft.Extensions.Logging;
using SharedBase.Utilities;
using Utilities;

/// <summary>
///   Fetches the launcher and Thrive version info from online or if failed potentially from a cached copy
/// </summary>
public class ThriveAndLauncherInfoRetriever : IThriveAndLauncherInfoRetriever
{
    private readonly ILogger<ThriveAndLauncherInfoRetriever> logger;
    private readonly INetworkDataRetriever networkDataRetriever;
    private readonly ILauncherPaths launcherPaths;

    public ThriveAndLauncherInfoRetriever(ILogger<ThriveAndLauncherInfoRetriever> logger,
        INetworkDataRetriever networkDataRetriever, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.networkDataRetriever = networkDataRetriever;
        this.launcherPaths = launcherPaths;
    }

    public bool IgnoreSigningRequirement { get; set; }

    public LauncherThriveInformation? CurrentlyLoadedInfo { get; private set; }

    public async Task<LauncherThriveInformation> DownloadInfo()
    {
        var url = LauncherConstants.LauncherInfoFileURL;

        Stream rawData;
        HttpStatusCode status;
        try
        {
            (status, rawData) =
                await networkDataRetriever.FetchNetworkResourceRaw(url);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to retrieve launcher info file from {Url} due to exception", url);
            throw new Exception($"Error: downloading launcher info file with Thrive versions: {e.Message}", null);
        }

        if (status != HttpStatusCode.OK)
        {
            logger.LogError("Failed to retrieve launcher info file from {Url}, received status code: {Status}", url,
                status);

            string content;

            try
            {
                using var textReader = new StreamReader(rawData, Encoding.UTF8);
                content = await textReader.ReadToEndAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Couldn't read error response content as UTF8");
                throw new Exception($"Error: got response code {status} with non-UTF8 content");
            }

            throw new Exception($"Error: got response code {status} with content: {content.Truncate(100)}");
        }

        var dataHandler = new SignedDataHandler();
        var (data, signature) = await dataHandler.ReadDataWithSignature(rawData);

        await CheckLauncherDataSignature(dataHandler, data, signature);

        var result = await DecodeLauncherInfo(data);

        try
        {
            await WriteToCache(dataHandler, data, signature);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to write downloaded launcher info to a cache file");
        }

        CurrentlyLoadedInfo = result;
        return result;
    }

    public bool HasCachedFile()
    {
        return File.Exists(launcherPaths.PathToCachedDownloadedLauncherInfo);
    }

    public async Task<LauncherThriveInformation?> LoadFromCache()
    {
        if (!HasCachedFile())
        {
            logger.LogError("Can't load cached launcher info as the file doesn't exist");
            return null;
        }

        var dataHandler = new SignedDataHandler();

        try
        {
            var (data, signature) =
                await dataHandler.ReadDataWithSignature(
                    File.OpenRead(launcherPaths.PathToCachedDownloadedLauncherInfo));

            await CheckLauncherDataSignature(dataHandler, data, signature);

            var result = await DecodeLauncherInfo(data);

            logger.LogDebug("Loaded cached info file {Path}", launcherPaths.PathToCachedDownloadedLauncherInfo);
            CurrentlyLoadedInfo = result;
            return result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load cached launcher info file");
            return null;
        }
    }

    private async Task CheckLauncherDataSignature(SignedDataHandler dataHandler, byte[] data, byte[] signature)
    {
        // ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162
        if (IgnoreSigningRequirement || LauncherConstants.Mode == LauncherConstants.LauncherMode.LocalTesting)
        {
            logger.LogWarning("Ignoring signing requirements, this is not very safe!");
            return;
        }

        var keys = new List<Tuple<Func<Task<byte[]>>, string>>();

        foreach (var resourceName in LauncherConstants.SigningManifestResourceNames)
        {
            keys.Add(new Tuple<Func<Task<byte[]>>, string>(
                () => ResourceUtilities.ReadManifestResourceRawAsync(resourceName), resourceName));
        }

        string? signedWith = null;
        try
        {
            signedWith = await dataHandler.VerifySignature(data, signature, keys);
        }
        catch (ArgumentException e)
        {
            throw new AllKeysExpiredException(e);
        }

        if (signedWith == null)
            throw new Exception("Invalid data signature. Has the data been corrupted?");

        logger.LogInformation("Downloaded launcher version info signed with {SignedWith}", signedWith);

        // ReSharper restore HeuristicUnreachableCode
#pragma warning restore CS0162
    }

    private async Task<LauncherThriveInformation> DecodeLauncherInfo(byte[] data)
    {
        using var uncompressedStream = new MemoryStream();

        {
            using var compressedDataStream = new MemoryStream(data, false);

            await using var decompressor = new BrotliStream(compressedDataStream, CompressionMode.Decompress, true);

            await decompressor.CopyToAsync(uncompressedStream);

            uncompressedStream.Position = 0;
        }

        return await JsonSerializer.DeserializeAsync<LauncherThriveInformation>(uncompressedStream) ??
            throw new NullDecodedJsonException();
    }

    private async Task WriteToCache(SignedDataHandler dataHandler, byte[] data, byte[] signature)
    {
        var path = launcherPaths.PathToCachedDownloadedLauncherInfo;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ??
            throw new Exception("Unknown folder to write info file to"));

        await using var writer = File.OpenWrite(path);

        using var dataStream = new MemoryStream(data, false);

        await dataHandler.WriteDataWithSignature(writer, dataStream, signature);

        logger.LogInformation("Saving a cached copy of downloaded info to: {Path}", path);
    }
}

public interface IThriveAndLauncherInfoRetriever
{
    public bool IgnoreSigningRequirement { get; set; }

    public LauncherThriveInformation? CurrentlyLoadedInfo { get; }

    public Task<LauncherThriveInformation> DownloadInfo();

    public bool HasCachedFile();

    public Task<LauncherThriveInformation?> LoadFromCache();
}
