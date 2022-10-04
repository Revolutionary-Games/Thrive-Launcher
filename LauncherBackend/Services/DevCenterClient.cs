namespace LauncherBackend.Services;

using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Models;

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

    public Task<bool> CheckDevCenterConnection()
    {
        throw new NotImplementedException();
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

    public Task<bool> CheckDevCenterConnection();
}
