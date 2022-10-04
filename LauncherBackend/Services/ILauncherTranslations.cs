namespace LauncherBackend.Services;

/// <summary>
///   Interface for concrete launcher implementations to provide translations to the generic parts
/// </summary>
public interface ILauncherTranslations
{
    public string StoreVersionName { get; }
    public string VersionWithPlatform { get; }
    public string LatestVersionTag { get; }
}
