namespace LauncherBackend.Models;

using DevCenterCommunication.Models;

/// <summary>
///   A normal (launcher info converted) playable version
/// </summary>
public class PlayableVersion : IPlayableVersion
{
    public PlayableVersion(string formattedVersionWithArch, string plainVersionNumber, DownloadableInfo versionDownload,
        bool isLatest, string folderName)
    {
        VersionName = formattedVersionWithArch;
        PlainVersionNumber = plainVersionNumber;
        IsLatest = isLatest;
        FolderName = folderName;
        Download = versionDownload;

        if (string.IsNullOrWhiteSpace(FolderName))
            throw new ArgumentException("Empty folder name");
    }

    public bool IsLatest { get; }

    public string VersionName { get; }

    public string FolderName { get; }

    public bool IsStoreVersion => false;
    public bool IsDevBuild => false;
    public bool IsPublicBuildA => false;
    public bool IsPublicBuildB => false;
    public bool IsPublicBuildC => false;

    public DownloadableInfo Download { get; }

    /// <summary>
    ///   The plain version number (for example 1.0.2.4). In contrast the <see cref="VersionName"/> contains the
    ///   platform etc. info.
    /// </summary>
    public string PlainVersionNumber { get; }
}
