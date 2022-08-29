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
    private const string ReadMoreLink = "Read More";

    // Aside can be disabled by

    /// <summary>
    ///   Text before an aside part starts. Can be disabled by being empty, probably wanted as having the aside
    ///   place content causes a bunch of extra spacing for the divs etc. elements after it
    /// </summary>
    private const string AsideStart = "";

    private readonly ILogger<HtmlSummaryParser> logger;
    private readonly HtmlParser htmlParser;
    private readonly StringBuilder stringBuilder;
    private readonly HashSet<INode> purposefullyProcessedNodes = new();

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

    private bool PendingText => stringBuilder.Length > 0;

    public ParsedLauncherFeedItem ParseItem(ParsedFeedItem item)
    {
        stringBuilder.Clear();
        purposefullyProcessedNodes.Clear();

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
                if (truncated || seenLength >= length)
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
                        // Ignore text within a link
                        var ancestorA = node.Ancestors<IHtmlAnchorElement>().FirstOrDefault();
                        if (ancestorA != null)
                        {
                            // For some reason aside github links don't have the link so we hack around that here
                            // by processing the ancestor purposefully here if it wasn't processed already
                            HandleAElement(ancestorA, ref seenLength, length);
                            break;
                        }

                        var textToAdd = text.Data;

                        if (seenLength + text.Data.Length > length)
                        {
                            textToAdd = text.Data.Truncate(length - seenLength);
                            seenLength += text.Data.Length;
                            truncated = true;
                        }

                        if (stringBuilder.Length < 1)
                        {
                            // Strip preceding whitespace to not have weird text blocks after links
                            textToAdd = textToAdd.TrimStart();
                        }

                        // TODO: Github issue links should be handled better, now a ton of vertical space gets inserted
                        // into them

                        // Avoid a bunch of useless whitespace
                        if (string.IsNullOrWhiteSpace(textToAdd))
                        {
                            if (PendingText)
                            {
                                var newLines = textToAdd.Count(c => c == '\n');
                                if (newLines > 0)
                                {
                                    if (CountEndingCharactersMatching('\n') < 3)
                                    {
                                        stringBuilder.Append('\n');
                                    }
                                }
                                else
                                {
                                    AddLastTextIfDoesNotEndWithAlready(" ");
                                }
                            }
                        }
                        else
                        {
                            stringBuilder.Append(textToAdd);
                        }

                        break;
                    }
                    case IHtmlUnorderedListElement or IHtmlOrderedListElement:
                        // These are handled in the below case
                        break;

                    case IHtmlListItemElement:
                    {
                        AddLastTextIfDoesNotEndWithAlready("\n");
                        stringBuilder.Append("- ");
                        break;
                    }
                    case IHtmlParagraphElement or IHtmlHeadingElement or IHtmlBreakRowElement:
                    {
                        if (PendingText)
                        {
                            AddLastTextIfDoesNotEndWithAlready("\n\n");
                        }

                        break;
                    }
                    case IHtmlQuoteElement:
                    {
                        AddLastTextIfDoesNotEndWithAlready("> ");

                        break;
                    }
                    case IHtmlSpanElement or IHtmlDivElement:
                    {
                        if (PendingText)
                            AddLastTextIfDoesNotEndWithAlready(" ");

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
                        HandleAElement(aElement, ref seenLength, length);
                        break;
                    }
                    case IHtmlImageElement:
                    {
                        // TODO: image support
                        truncated = true;
                        break;
                    }
                    case ISvgElement:
                    {
                        // TODO: svg support
                        truncated = true;
                        break;
                    }

                    default:
                    {
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

                        if (name == "aside")
                        {
                            if (!string.IsNullOrEmpty(AsideStart))
                            {
                                if (PendingText)
                                    AddLastTextIfDoesNotEndWithAlready("\n\n");

                                stringBuilder.Append(AsideStart);
                            }

                            continue;
                        }

                        // Unknown node, so we are losing info here
                        truncated = true;
                        FlushTextIfPending();

                        logger.LogDebug("Not parsing unknown HTML in feed: {Node}", node);
                        break;
                    }
                }
            }
        }

        FlushTextIfPending();
        FinishCurrentItem();

        result.Truncated = truncated;
        return result;
    }

    private void HandleAElement(IHtmlAnchorElement aElement, ref int seenLength, int length)
    {
        // Skip if already processed
        if (!purposefullyProcessedNodes.Add(aElement))
            return;

        FlushTextIfPending();

        FinishCurrentItem();

        var workedOnItem = new Models.ParsedContent.Link(aElement.Href);
        currentItem = workedOnItem;

        if (!string.IsNullOrEmpty(aElement.Text))
            workedOnItem.Text = aElement.Text;

        // Stop processing stuff after a read more link as WordPress has a ton of useless stuff after
        // that
        if (workedOnItem.Text.Contains(ReadMoreLink))
            seenLength = length;
    }

    private void AddLastTextIfDoesNotEndWithAlready(string text)
    {
        if (stringBuilder.Length < text.Length)
        {
            stringBuilder.Append(text);
        }

        bool match = true;

        for (int i = text.Length; i > 0; --i)
        {
            if (stringBuilder[^i] != text[^i])
            {
                match = false;
                break;
            }
        }

        if (!match)
            stringBuilder.Append(text);
    }

    private int CountEndingCharactersMatching(char character)
    {
        if (stringBuilder.Length < 1)
            return 0;

        int count = 0;

        for (int i = stringBuilder.Length; i > 0; --i)
        {
            if (stringBuilder[^i] == character)
            {
                ++count;
            }
            else
            {
                break;
            }
        }

        return count;
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
