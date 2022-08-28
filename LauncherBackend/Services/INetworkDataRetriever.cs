using System.Net;

namespace LauncherBackend.Services;

public interface INetworkDataRetriever
{
    public Task<(HttpStatusCode Status, string Content)> FetchNetworkResource(Uri uri);
}
