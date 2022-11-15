namespace ThriveLauncher.Services;

using LauncherBackend.Services;
using Properties;

/// <summary>
///   Converts from <see cref="ILauncherTranslations"/> to <see cref="Resources"/>
/// </summary>
public class LauncherTranslationProxy : ILauncherTranslations
{
    public string StoreVersionName => Resources.StoreVersionName;
    public string VersionWithPlatform => Resources.VersionWithPlatform;
    public string LatestVersionTag => Resources.LatestVersionTag;
}
