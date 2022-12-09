namespace LauncherBackend.Services;

using Models;

public interface ILauncherFeeds
{
    public Task<(string? Error, List<ParsedLauncherFeedItem>? Items)> FetchFeed(string name, Uri url);
}
