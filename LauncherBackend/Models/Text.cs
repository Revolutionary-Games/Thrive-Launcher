namespace LauncherBackend.Models.ParsedContent;

public class Text : ParsedFeedContent
{
    public Text(string content)
    {
        Content = content;
    }

    public string Content { get; set; }
}
