namespace LauncherBackend.Models;

/// <summary>
///   Represents a folder (Thrive or some random folder) in the Thrive install folder
/// </summary>
public class FolderInInstallFolder
{
    public FolderInInstallFolder(string path, bool isThriveFolder)
    {
        Path = path;
        IsThriveFolder = isThriveFolder;
    }

    public string Path { get; }

    public string FolderName => System.IO.Path.GetFileName(Path);

    public bool IsThriveFolder { get; }

    /// <summary>
    ///   Size in bytes. Only set for Thrive related folders
    /// </summary>
    public long Size { get; set; }
}
