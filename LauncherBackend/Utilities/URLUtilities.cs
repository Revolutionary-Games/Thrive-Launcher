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

    public static void OpenLocalFileOrFolder(string fileOrFolder)
    {
        if (!fileOrFolder.StartsWith("file://"))
            fileOrFolder = $"file://{Path.GetFullPath(fileOrFolder)}";

        // TODO: test on all platforms, not tested on Windows or mac
        Process.Start(new ProcessStartInfo
        {
            FileName = fileOrFolder,
            UseShellExecute = true,
            Verb = "open",
        });
    }
}
