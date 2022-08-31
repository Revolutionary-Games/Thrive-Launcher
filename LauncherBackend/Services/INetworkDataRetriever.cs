namespace LauncherBackend.Services;

using System.Net;

public interface INetworkDataRetriever
{
    public Task<(HttpStatusCode Status, string Content)> FetchNetworkResource(Uri uri);
}
