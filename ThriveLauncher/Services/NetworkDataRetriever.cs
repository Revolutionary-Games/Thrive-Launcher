namespace ThriveLauncher.Services;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using Utilities;

public class NetworkDataRetriever : INetworkDataRetriever
{
    private readonly ILogger<NetworkDataRetriever> logger;
    private readonly VersionUtilities versionUtilities;

    public NetworkDataRetriever(ILogger<NetworkDataRetriever> logger, VersionUtilities versionUtilities)
    {
        this.logger = logger;
        this.versionUtilities = versionUtilities;
    }

    public ProductInfoHeaderValue UserAgent => new("Thrive-Launcher", versionUtilities.LauncherVersion);

    public async Task<(HttpStatusCode Status, string Content)> FetchNetworkResource(Uri uri)
    {
        using var client = CreateHttpClient();

        logger.LogDebug("Fetching: {Uri}", uri);
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        var content = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, content);
    }

    public async Task<(HttpStatusCode Status, Stream Content)> FetchNetworkResourceRaw(Uri uri)
    {
        using var client = CreateHttpClient();

        logger.LogDebug("Fetching: {Uri}", uri);
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        var content = await response.Content.ReadAsStreamAsync();

        return (response.StatusCode, content);
    }

    private HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
        client.Timeout = TimeSpan.FromMinutes(1);

        return client;
    }
}
