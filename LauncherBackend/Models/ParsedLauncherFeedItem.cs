namespace LauncherBackend.Models;

using System.Collections.Generic;
using FeedParser.Models;

public class ParsedLauncherFeedItem : ParsedFeedItem
{
    public ParsedLauncherFeedItem(string id, string link, string title, string author) : base(id, link, title, author)
    {
    }

    public List<ParsedFeedContent> ParsedSummary { get; } = new();

    /// <summary>
    ///   True when <see cref="ParsedSummary"/> is truncated or some (important) HTML data could not be translated
    /// </summary>
    public bool Truncated { get; set; }
}
