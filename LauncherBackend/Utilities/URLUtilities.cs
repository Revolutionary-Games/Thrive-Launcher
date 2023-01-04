namespace LauncherBackend.Utilities;

using System.Diagnostics;

public static class URLUtilities
{
    public static void OpenURLInBrowser(string url)
    {
        if (!url.StartsWith("http"))
            throw new ArgumentException("Url needs to start with http");

        // TODO: do we need to check the returned process for errors?
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
            Verb = "open",
        });
    }
}
