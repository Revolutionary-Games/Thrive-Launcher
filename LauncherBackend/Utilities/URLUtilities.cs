namespace LauncherBackend.Utilities;

using System;
using System.Diagnostics;

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
