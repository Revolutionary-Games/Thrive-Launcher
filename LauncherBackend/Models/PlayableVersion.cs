namespace LauncherBackend.Models;

using DevCenterCommunication.Models;

/// <summary>
///   A normal (launcher info converted) playable version
/// </summary>
public class PlayableVersion : IPlayableVersion
{
    private readonly ThriveVersionLauncherInfo launcherVersion;

    public PlayableVersion(string formattedVersionWithArch, ThriveVersionLauncherInfo launcherVersion, bool isLatest)
    {
        VersionName = formattedVersionWithArch;
        IsLatest = isLatest;
        this.launcherVersion = launcherVersion;
    }

    public bool IsLatest { get; }

    public string VersionName { get; }

    public bool IsStoreVersion => false;
    public bool IsDevBuild => false;
    public bool IsPublicBuildA => false;
    public bool IsPublicBuildB => false;
    public bool IsPublicBuildC => false;
}
