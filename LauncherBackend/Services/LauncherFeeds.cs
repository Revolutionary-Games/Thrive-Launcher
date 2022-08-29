using System.Net;
using FeedParser.Models;
using FeedParser.Shared.Models;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using SharedBase.Utilities;

namespace LauncherBackend.Services;

public class LauncherFeeds : ILauncherFeeds
{
    private readonly ILogger<LauncherFeeds> logger;
    private readonly INetworkDataRetriever networkDataRetriever;
    private readonly IHtmlSummaryParser summaryParser;

    public LauncherFeeds(ILogger<LauncherFeeds> logger, INetworkDataRetriever networkDataRetriever,
        IHtmlSummaryParser summaryParser)
    {
        this.logger = logger;
        this.networkDataRetriever = networkDataRetriever;
        this.summaryParser = summaryParser;
    }

    /// <summary>
    ///   Replaces the username + full name combo in discourse message authors with just the username
    /// </summary>
    public static string GetPosterUsernameToDisplay(ParsedFeedItem item)
    {
        if (item.Author.StartsWith('@'))
        {
            return item.Author.Split(' ').First();
        }

        return item.Author;
    }

    public async Task<(string? Error, List<ParsedLauncherFeedItem>? Items)> FetchFeed(string name, Uri url)
    {
        string rawData;
        try
        {
            (var status, rawData) = await networkDataRetriever.FetchNetworkResource(url);

            if (status != HttpStatusCode.OK)
            {
                logger.LogError("Failed to retrieve feed {Url}, received status code: {Status}", url, status);
                return ($"Error: got response code {status} with content: {rawData.Truncate(100)}", null);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to retrieve feed {Url} due to exception", url);
            return ($"Error: downloading feed data failed: {e}", null);
        }

        var feed = new LauncherFeed(name);

        List<ParsedLauncherFeedItem> secondParse;
        try
        {
            var firstParse = FeedParser.Services.FeedParser.ParseContent(feed, rawData, out _);

            lock (summaryParser)
            {
                secondParse = firstParse.Select(summaryParser.ParseItem).ToList();
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse feed {Url}", url);
            return ($"Error: parsing feed data failed: {e}", null);
        }

        return (null, secondParse);
    }
}

public interface ILauncherFeeds
{
    public Task<(string? Error, List<ParsedLauncherFeedItem>? Items)> FetchFeed(string name, Uri url);
}

internal class LauncherFeed : IFeed
{
    public LauncherFeed(string name)
    {
        Name = name;
    }

    public string Name { get; set; }
    public int MaxItems => int.MaxValue;
    public string? LatestContent { get; set; }
    public DateTime? ContentUpdatedAt { get; set; }
    public List<FeedPreprocessingAction>? PreprocessingActions => null;
    public string? HtmlFeedItemEntryTemplate => null;
    public string? HtmlFeedVersionSuffix => null;
    public string? HtmlLatestContent { get; set; }
    public int MaxItemLength => int.MaxValue;
    public int LatestContentHash { get; set; }
}
