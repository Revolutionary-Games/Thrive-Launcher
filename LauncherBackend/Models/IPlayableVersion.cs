namespace LauncherBackend.Models;

public interface IPlayableVersion
{
    public string VersionName { get; }

    public string FolderName { get; }

    public bool IsStoreVersion { get; }
    public bool IsDevBuild { get; }

    // For later use:
    public bool IsPublicBuildA { get; }
    public bool IsPublicBuildB { get; }
    public bool IsPublicBuildC { get; }
}

public class StoreVersion : IPlayableVersion
{
    private readonly string storeName;

    public StoreVersion(string storeName)
    {
        this.storeName = storeName;
    }

    public string VersionName => storeName;

    public string FolderName => "./";

    public bool IsStoreVersion => true;

    public bool IsDevBuild => false;
    public bool IsPublicBuildA => false;
    public bool IsPublicBuildB => false;
    public bool IsPublicBuildC => false;
}

public class DevBuildVersion : IPlayableVersion
{
    private readonly PlayableDevCenterBuildType type;

    public DevBuildVersion(PlayableDevCenterBuildType type)
    {
        this.type = type;
    }

    public string VersionName => type switch
    {
        PlayableDevCenterBuildType.DevBuild => "DevBuild",
        PlayableDevCenterBuildType.PublicBuildA => "Public Test A",
        PlayableDevCenterBuildType.PublicBuildB => "Public Test B",
        PlayableDevCenterBuildType.PublicBuildC => "Public Test C",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public bool IsStoreVersion => false;

    public bool IsDevBuild => true;

    public string FolderName => type switch
    {
        PlayableDevCenterBuildType.DevBuild => "devbuild",
        PlayableDevCenterBuildType.PublicBuildA => "devbuild_test_a",
        PlayableDevCenterBuildType.PublicBuildB => "devbuild_test_b",
        PlayableDevCenterBuildType.PublicBuildC => "devbuild_test_c",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public bool IsPublicBuildA => type == PlayableDevCenterBuildType.PublicBuildA;
    public bool IsPublicBuildB => type == PlayableDevCenterBuildType.PublicBuildB;
    public bool IsPublicBuildC => type == PlayableDevCenterBuildType.PublicBuildC;

    public bool IsPublicBuild => IsPublicBuildA || IsPublicBuildB || IsPublicBuildC;

    public PlayableDevCenterBuildType BuildType => type;
}
