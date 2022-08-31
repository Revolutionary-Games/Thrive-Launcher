namespace LauncherBackend.Utilities;

using System.Diagnostics;

public static class FileUtilities
{
    /// <summary>
    ///   Calculates the size of all files in a folder recursively
    /// </summary>
    /// <param name="path">The folder to calculate size for</param>
    /// <returns>The size in bytes</returns>
    public static long CalculateFolderSize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        long size = 0;

        foreach (var file in Directory.EnumerateFiles(path))
        {
            size += new FileInfo(file).Length;
        }

        foreach (var folder in Directory.EnumerateDirectories(path))
        {
            size += CalculateFolderSize(folder);
        }

        return size;
    }

    /// <summary>
    ///   Opens a folder in the current platform's default viewer (explorer.exe, a Linux file browser etc.)
    /// </summary>
    /// <param name="folder">The folder to open</param>
    public static void OpenFolderInPlatformSpecificViewer(string folder)
    {
        folder = folder.Replace('/', Path.DirectorySeparatorChar);

        if (!folder.EndsWith(Path.DirectorySeparatorChar))
            folder += Path.DirectorySeparatorChar;

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
            Verb = "open",
        });
    }
}
