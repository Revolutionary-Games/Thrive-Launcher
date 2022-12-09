namespace LauncherBackend.Services;

using FeedParser.Models;
using Models;

public interface IHtmlSummaryParser
{
    public ParsedLauncherFeedItem ParseItem(ParsedFeedItem item);
}
