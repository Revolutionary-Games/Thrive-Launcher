namespace LauncherBackend.Services;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

public class DevCenterClient : IDevCenterClient
{
    private readonly ILogger<DevCenterClient> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly HttpClient httpClient;
    private readonly HttpClient publicHttpClient;

    public DevCenterClient(ILogger<DevCenterClient> logger, ILauncherSettingsManager settingsManager)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;

        httpClient = new HttpClient();
        httpClient.BaseAddress = LauncherConstants.DevCenterURL;
        httpClient.Timeout = TimeSpan.FromMinutes(1);
        SetAuthorization();

        publicHttpClient = new HttpClient();
        httpClient.BaseAddress = LauncherConstants.DevCenterURL;
        httpClient.Timeout = TimeSpan.FromMinutes(1);
    }

    public DevCenterConnection? DevCenterConnection { get; private set; }

    private Uri LauncherCheckAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/status");
    private Uri LauncherTestTokenAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/check_link");
    private Uri LauncherFormConnectionAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/link");
    private Uri LauncherFindAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/find");
    private Uri LauncherBuildsListAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/builds");
    private Uri LauncherSearchAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/search");

    private Uri LauncherDownloadBuildAPI => new(LauncherConstants.DevCenterURL, "/api/v1/launcher/builds/download/");

    private Uri LauncherDownloadDehydratedAPI =>
        new(LauncherConstants.DevCenterURL, "/api/v1/launcher/dehydrated/download");

    private void SetAuthorization()
    {
        if (string.IsNullOrEmpty(settingsManager.Settings.DevCenterKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(settingsManager.Settings.DevCenterKey);
        }
    }

    /// <summary>
    ///   Checks the current connection and updates <see cref="DevCenterConnection"/>
    /// </summary>
    /// <returns>
    ///   <see cref="DevCenterResult.Success"/> if successful, an error otherwise.
    ///   Not being logged in is not an error.
    /// </returns>
    public async Task<DevCenterResult> CheckDevCenterConnection()
    {
        if (string.IsNullOrEmpty(settingsManager.Settings.DevCenterKey))
        {
            logger.LogInformation("No devcenter key stored");
            DevCenterConnection = null;
            return DevCenterResult.Success;
        }

        // If our connection has been changed, update that here. We don't update this elsewhere as checking if the
        // connection is valid should always be done through this method
        SetAuthorization();

        try
        {
            var response = await httpClient.GetFromJsonAsync<LauncherConnectionStatus>(LauncherCheckAPI) ??
                throw new NullDecodedJsonException();

            if (!response.Valid)
            {
                DevCenterConnection = null;
                return DevCenterResult.InvalidKey;
            }

            logger.LogInformation("We have connection to the DevCenter as {Username}", response.Username);

            DevCenterConnection = new DevCenterConnection(response);
            return DevCenterResult.Success;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter status link check failed");
            return HandleRequestError(e);
        }
    }

    public async Task<DevBuildLauncherDTO?> QueryFindAPI(DevBuildFindByTypeForm.BuildType buildType)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(LauncherFindAPI, new DevBuildFindByTypeForm
            {
                Platform = GetDevBuildPlatformForCurrentPlatform(),
                Type = buildType,
            });

            response.EnsureSuccessStatusCode();

            var result = await JsonSerializer.DeserializeAsync<DevBuildLauncherDTO>(
                    await response.Content.ReadAsStreamAsync()) ??
                throw new NullDecodedJsonException();

            logger.LogDebug("Found build with query: {Id}", result.Id);

            return result;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter query failed");
            return null;
        }
    }

    public async Task<DevBuildLauncherDTO?> QuerySearchAPI(string buildHash)
    {
        if (string.IsNullOrWhiteSpace(buildHash))
            throw new ArgumentException("Invalid hash to search for (it is empty)");

        try
        {
            var response = await httpClient.PostAsJsonAsync(LauncherSearchAPI, new DevBuildHashSearchForm
            {
                Platform = GetDevBuildPlatformForCurrentPlatform(),
                BuildHash = buildHash,
            });

            response.EnsureSuccessStatusCode();

            var result = await JsonSerializer.DeserializeAsync<DevBuildSearchResults>(
                    await response.Content.ReadAsStreamAsync()) ??
                throw new NullDecodedJsonException();

            logger.LogDebug("Found {Count} build(s) with search, for hash: {BuildHash}", result.Result.Count,
                buildHash);

            if (result.Result.Count < 1)
                return null;

            DevBuildLauncherDTO? mostPromising = null;

            // Find the most promising entry
            foreach (var potentialBuild in result.Result)
            {
                if (mostPromising == null)
                {
                    mostPromising = potentialBuild;
                    continue;
                }

                if (mostPromising.Anonymous && !potentialBuild.Anonymous)
                {
                    mostPromising = potentialBuild;
                    continue;
                }

                if (!mostPromising.Verified && potentialBuild.Verified)
                {
                    mostPromising = potentialBuild;
                }
            }

            return mostPromising;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter query failed");
            return null;
        }
    }

    public Task<DevBuildLauncherDTO?> FetchBuildWeWantToPlay()
    {
        switch (settingsManager.Settings.SelectedDevBuildType)
        {
            case DevBuildType.BuildOfTheDay:
                return QueryFindAPI(DevBuildFindByTypeForm.BuildType.BuildOfTheDay);
            case DevBuildType.Latest:
                return QueryFindAPI(DevBuildFindByTypeForm.BuildType.Latest);
            case DevBuildType.ManuallySelected:
                if (string.IsNullOrWhiteSpace(settingsManager.Settings.ManuallySelectedBuildHash))
                    throw new Exception("No manually selected hash entered. Please select one and try again");

                return QuerySearchAPI(settingsManager.Settings.ManuallySelectedBuildHash);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task<DevBuildDownload?> GetDownloadForBuild(DevBuildLauncherDTO build)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<DevBuildDownload>(LauncherDownloadBuildAPI +
                build.Id.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter getting download for build failed");
            return null;
        }
    }

    public async Task<List<DehydratedObjectDownloads.DehydratedObjectDownload>?> GetDownloadsForDehydrated(
        IEnumerable<DehydratedObjectIdentification> dehydratedObjects)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(LauncherDownloadDehydratedAPI,
                new DevBuildDehydratedObjectDownloadRequest
                {
                    Objects = dehydratedObjects.ToList(),
                });

            response.EnsureSuccessStatusCode();

            var result = await JsonSerializer.DeserializeAsync<DehydratedObjectDownloads>(
                    await response.Content.ReadAsStreamAsync()) ??
                throw new NullDecodedJsonException();

            if (result.Downloads.Count < 1)
                return null;

            logger.LogDebug("Got {Count} dehydrated object download response(s)", result.Downloads.Count);

            return result.Downloads;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter getting downloads for list of dehydrated objects failed");
            return null;
        }
    }

    public async Task<List<DevBuildLauncherDTO>?> FetchLatestBuilds(int offset, int pageSize)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(LauncherBuildsListAPI,
                new DevBuildSearchForm
                {
                    Platform = GetDevBuildPlatformForCurrentPlatform(),
                    Offset = offset,
                    PageSize = pageSize,
                });

            response.EnsureSuccessStatusCode();

            var result = await JsonSerializer.DeserializeAsync<DevBuildSearchResults>(
                    await response.Content.ReadAsStreamAsync()) ??
                throw new NullDecodedJsonException();

            logger.LogDebug("Got {Count} latest builds at offset {Offset}", result.Result.Count, offset);

            // TODO: pass the result.NextOffset out of this method if pagination is wanted

            return result.Result;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter getting latest builds failed");
            return null;
        }
    }

    /// <summary>
    ///   Forms a permanent DevCenter connection. After this succeeds <see cref="CheckDevCenterConnection"/> should
    ///   be called again.
    /// </summary>
    /// <param name="code">The code to consume</param>
    /// <returns>Success result when connection was formed</returns>
    public async Task<DevCenterResult> FormConnection(string code)
    {
        try
        {
            var response =
                await publicHttpClient.PostAsJsonAsync(LauncherFormConnectionAPI, new LauncherLinkCodeCheckForm(code));

            response.EnsureSuccessStatusCode();

            var result =
                await JsonSerializer.DeserializeAsync<LauncherLinkResult>(await response.Content.ReadAsStreamAsync()) ??
                throw new NullDecodedJsonException();

            if (!result.Connected || string.IsNullOrEmpty(result.Code))
            {
                logger.LogInformation("Connect result has connected field as false (or missing token)");
                return DevCenterResult.InvalidKey;
            }

            logger.LogInformation("We have formed a new connection to the DevCenter");

            await SaveTokenInSettings(result.Code);
            return DevCenterResult.Success;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter form connection failed");
            return HandleRequestError(e);
        }
    }

    /// <summary>
    ///   Checks a DevCenter connection forming code
    /// </summary>
    /// <param name="code">The code to check</param>
    /// <returns>The data regarding the potential connection or null if the code is invalid and an error</returns>
    public async Task<(LauncherConnectionStatus? status, string? error)> CheckLinkCode(string code)
    {
        try
        {
            var response =
                await publicHttpClient.PostAsJsonAsync(LauncherTestTokenAPI, new LauncherLinkCodeCheckForm(code));

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                try
                {
                    var parsed = (JsonObject?)JsonNode.Parse(content);

                    if (parsed != null && parsed.TryGetPropertyValue("message", out var messageNode) &&
                        messageNode != null)
                    {
                        content = messageNode.GetValue<string>();
                    }
                }
                catch (Exception e)
                {
                    logger.LogInformation(e, "Server response not in expected JSON form, showing error with raw data");
                }

                return (null, $"Server responded: {content}");
            }

            var result =
                await JsonSerializer.DeserializeAsync<LauncherConnectionStatus>(
                    await response.Content.ReadAsStreamAsync()) ?? throw new NullDecodedJsonException();

            if (!result.Valid)
                return (null, "Server said code is not valid in an unexpected way");

            logger.LogInformation("We have a valid code to the DevCenter");

            return (result, null);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DevCenter link code checking failed (likely due to connection problem)");
            return (null, "Unknown / connection error");
        }
    }

    public async Task Logout()
    {
        logger.LogInformation("Disconnecting from the DevCenter");

        try
        {
            var response = await httpClient.DeleteAsync(LauncherCheckAPI);

            response.EnsureSuccessStatusCode();

            // We don't check the result here as the status code already tells everything
            // TODO: launcher API v2 that doesn't return this pointless data

            logger.LogInformation("Successfully told DevCenter that we are disconnecting");
        }
        catch (Exception e)
        {
            logger.LogWarning(e,
                "DevCenter logout request failed. We'll still clear our local data " +
                "but the DevCenter won't know about that");
        }

        DevCenterConnection = null;

        await ClearTokenInSettings();

        SetAuthorization();
    }

    public async Task ClearTokenInSettings()
    {
        logger.LogInformation("Clearing our DevCenter key");

        settingsManager.Settings.DevCenterKey = null;
        if (!await settingsManager.Save())
        {
            logger.LogError("Failed to save settings after clearing connection key");
        }
    }

    public async Task SaveTokenInSettings(string token)
    {
        settingsManager.Settings.DevCenterKey = token;
        if (!await settingsManager.Save())
        {
            logger.LogError("Failed to save settings after getting a new connection key");
        }
    }

    public string GetDevBuildPlatformForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return "Windows Desktop";
            }

            return "Windows Desktop (32-bit)";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux/X11";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Mac OSX";
        }

        return $"Error unknown platform for DevBuilds: {Environment.OSVersion}";
    }

    private DevCenterResult HandleRequestError(Exception e)
    {
        if (e is HttpRequestException httpException)
        {
            logger.LogDebug("DevCenter request failed with a http exception");

            if (httpException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("DevCenter responded with forbidden or unauthorized response");
                return DevCenterResult.InvalidKey;
            }

            logger.LogInformation("We likely couldn't connect to the DevCenter or it is down");
            return DevCenterResult.ConnectionFailure;
        }

        logger.LogWarning("Encountered a data error / another logic error we can't handle from the DevCenter");
        return DevCenterResult.DataError;
    }
}

/// <summary>
///   Manages connecting to the DevCenter and performing DevCenter operations
/// </summary>
public interface IDevCenterClient
{
    /// <summary>
    ///   The current DevCenter connection info
    /// </summary>
    public DevCenterConnection? DevCenterConnection { get; }

    public Task<DevCenterResult> CheckDevCenterConnection();

    /// <summary>
    ///   Retrieves info from the devcenter about the build we want to play
    /// </summary>
    /// <returns>The build to play or null</returns>
    /// <exception cref="Exception">If manually selected hash is selected but none is set in settings</exception>
    public Task<DevBuildLauncherDTO?> FetchBuildWeWantToPlay();

    /// <summary>
    ///   Gets download info for a build
    /// </summary>
    /// <param name="build">The build to get a download for</param>
    /// <returns>The download info or null if there was an error retrieving the data</returns>
    public Task<DevBuildDownload?> GetDownloadForBuild(DevBuildLauncherDTO build);

    /// <summary>
    ///   Gets download urls for dehydrated objects based on on their hashes
    /// </summary>
    /// <param name="dehydratedObjects">The objects to ask downloads for</param>
    /// <returns>If successful list of downloads related to the requested hashes</returns>
    public Task<List<DehydratedObjectDownloads.DehydratedObjectDownload>?> GetDownloadsForDehydrated(
        IEnumerable<DehydratedObjectIdentification> dehydratedObjects);

    /// <summary>
    ///   Queries the devcenter for the latest builds
    /// </summary>
    /// <param name="offset">Offset to use (start at 0)</param>
    /// <param name="pageSize">The page size to fetch at once</param>
    /// <returns>The found latest builds or null on error</returns>
    public Task<List<DevBuildLauncherDTO>?> FetchLatestBuilds(int offset, int pageSize = 75);

    public Task<DevCenterResult> FormConnection(string code);

    public Task<(LauncherConnectionStatus? status, string? error)> CheckLinkCode(string code);

    public Task Logout();

    public Task ClearTokenInSettings();
}
