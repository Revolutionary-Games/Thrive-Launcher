using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Svg.Dom;
using FeedParser.Models;
using FeedParser.Utilities;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using SharedBase.Utilities;

namespace LauncherBackend.Services;

public class HtmlSummaryParser : IHtmlSummaryParser
{
    private readonly ILogger<HtmlSummaryParser> logger;
    private readonly HtmlParser htmlParser;
    private readonly StringBuilder stringBuilder;

    private ParsedLauncherFeedItem? result;
    private ParsedFeedContent? currentItem;

    public HtmlSummaryParser(ILogger<HtmlSummaryParser> logger)
    {
        this.logger = logger;

        htmlParser = new HtmlParser(new HtmlParserOptions
        {
            IsStrictMode = false,
        });

        stringBuilder = new StringBuilder(500);
    }

    public ParsedLauncherFeedItem ParseItem(ParsedFeedItem item)
    {
        stringBuilder.Clear();

        result = new ParsedLauncherFeedItem(item.Id, item.Link, item.Title, item.Author)
        {
            Link = item.Link,
            PublishedAt = item.PublishedAt,
            Summary = item.Summary,
            OriginalFeed = item.OriginalFeed,
        };

        if (string.IsNullOrEmpty(item.Summary))
            return result;

        var document = htmlParser.ParseFragment(item.Summary, HtmlStringExtensions.CreateDummyDom());

        int length = LauncherConstants.FeedExcerptLength;
        int seenLength = 0;

        // Used to add the truncated info if we removed excess text or non-displayable content
        bool truncated = false;

        foreach (var topLevelNode in document)
        {
            foreach (var node in topLevelNode.GetDescendants())
            {
                if (truncated)
                {
                    // If we encounter a non-text node before we are full on text, we don't want to skip all remaining
                    // text nodes
                    if (node is not IText || seenLength >= length)
                        continue;
                }

                switch (node)
                {
                    case IText text:
                    {
                        var textToAdd = text.Data;

                        if (seenLength + text.Data.Length > length)
                        {
                            textToAdd = text.Data.Truncate(length - seenLength);
                            seenLength += text.Data.Length;
                            truncated = true;
                        }

                        stringBuilder.Append(textToAdd);

                        break;
                    }
                    case IHtmlParagraphElement:
                    {
                        if (stringBuilder.Length > 0)
                            stringBuilder.Append('\n');

                        break;
                    }
                    case IHtmlSpanElement:
                    {
                        if (stringBuilder.Length > 0)
                            stringBuilder.Append(' ');

                        break;
                    }
                    case IHtmlInlineFrameElement iFrame:
                    {
                        FlushTextIfPending();
                        var match = LauncherConstants.YoutubeURLRegex.Match(iFrame.Source ?? string.Empty);

                        if (match.Success)
                        {
                            FinishCurrentItem();

                            currentItem = new Models.ParsedContent.Link(match.Groups[1].Value);
                        }
                        else
                        {
                            logger.LogDebug("Removing iframe to: {Source}", iFrame.Source);
                        }

                        break;
                    }
                    case IHtmlAnchorElement aElement:
                    {
                        FlushTextIfPending();

                        FinishCurrentItem();

                        var workedOnItem = new Models.ParsedContent.Link(aElement.Target ?? string.Empty);
                        currentItem = workedOnItem;

                        if (!string.IsNullOrEmpty(aElement.Text))
                            workedOnItem.Text = aElement.Text;

                        break;
                    }
                    case ISvgElement:
                    {
                        // TODO: svg support
                        truncated = true;
                        break;
                    }

                    default:
                        // Ignore semantic elements here
                        if ((node.Flags & NodeFlags.Special) != 0)
                            break;

                        var name = node.NodeName.ToLowerInvariant();

                        if (name == TagNames.Dd || name ==
                            TagNames.Dt || name ==
                            TagNames.B || name ==
                            TagNames.Big || name ==
                            TagNames.Strike || name ==
                            TagNames.Code || name ==
                            TagNames.Em || name ==
                            TagNames.I || name ==
                            TagNames.S || name ==
                            TagNames.Small || name ==
                            TagNames.Strong || name ==
                            TagNames.U || name ==
                            TagNames.Tt || name ==
                            TagNames.NoBr)
                        {
                            // TODO: implement text styling
                            continue;
                        }

                        // Unknown node, so we are losing info here
                        truncated = true;
                        FlushTextIfPending();

                        logger.LogDebug("Removing unknown HTML in feed: {Node}", node);
                        break;
                }
            }
        }

        FlushTextIfPending();
        FinishCurrentItem();

        result.Truncated = truncated;
        return result;
    }

    private void FlushTextIfPending()
    {
        if (stringBuilder.Length < 1)
            return;

        if (currentItem is Models.ParsedContent.Text currentText)
        {
            currentText.Content += stringBuilder.ToString();
        }
        else
        {
            FinishCurrentItem();
            currentItem = new Models.ParsedContent.Text(stringBuilder.ToString());
        }

        stringBuilder.Clear();
    }

    private void FinishCurrentItem()
    {
        if (currentItem != null)
            result!.ParsedSummary.Add(currentItem);

        currentItem = null;
    }
}

public interface IHtmlSummaryParser
{
    ParsedLauncherFeedItem ParseItem(ParsedFeedItem item);
}
