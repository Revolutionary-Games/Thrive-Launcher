namespace LauncherBackend.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

public class DevCenterClient : IDevCenterClient
{
    private readonly ILogger<DevCenterClient> logger;
    private readonly ILauncherSettingsManager settingsManager;
    private readonly HttpClient httpClient;

    public DevCenterClient(ILogger<DevCenterClient> logger, ILauncherSettingsManager settingsManager)
    {
        this.logger = logger;
        this.settingsManager = settingsManager;

        httpClient = new HttpClient();
        httpClient.BaseAddress = LauncherConstants.DevCenterURL;
        httpClient.Timeout = TimeSpan.FromMinutes(1);
        SetAuthorization();
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

    public async Task Logout()
    {
        // TODO: send a logout message?

        await ClearTokenInSettings();
    }

    public async Task ClearTokenInSettings()
    {
        settingsManager.Settings.DevCenterKey = null;
        if (!await settingsManager.Save())
        {
            logger.LogError("Failed to save settings after clearing connection key");
        }
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

    public Task Logout();

    public Task ClearTokenInSettings();
}
