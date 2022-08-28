using System.Diagnostics;

namespace LauncherBackend.Utilities;

public static class URLUtilities
{
    public static void OpenURLInBrowser(string url)
    {
        if (!url.StartsWith("http"))
            throw new ArgumentException("Url needs to start with http");

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
            Verb = "open",
        });
    }
}
