namespace LauncherBackend.Services;

using System.Net;
using System.Net.Http.Headers;

public interface INetworkDataRetriever
{
    public Task<(HttpStatusCode Status, string Content)> FetchNetworkResource(Uri uri);
    public Task<(HttpStatusCode Status, Stream Content)> FetchNetworkResourceRaw(Uri uri);

    public ProductInfoHeaderValue UserAgent { get; }
}
