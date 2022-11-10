namespace LauncherBackend.Services;

using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public interface INetworkDataRetriever
{
    public Task<(HttpStatusCode Status, string Content)> FetchNetworkResource(Uri uri);
    public Task<(HttpStatusCode Status, Stream Content)> FetchNetworkResourceRaw(Uri uri);

    public ProductInfoHeaderValue UserAgent { get; }
}
