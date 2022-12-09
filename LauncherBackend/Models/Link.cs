namespace LauncherBackend.Models.ParsedContent;

public class Link : ParsedFeedContent
{
    public Link(string target)
    {
        Target = target;
        Text = target;
    }

    public string Target { get; set; }
    public string Text { get; set; }
}
