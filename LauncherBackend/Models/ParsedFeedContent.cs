namespace LauncherBackend.Models
{
    public abstract class ParsedFeedContent
    {
    }

    namespace ParsedContent
    {
        public class Text : ParsedFeedContent
        {
            public Text(string content)
            {
                Content = content;
            }

            public string Content { get; set; }
        }

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
    }
}
