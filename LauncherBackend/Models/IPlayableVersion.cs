namespace LauncherBackend.Models;

using DevCenterCommunication.Models;

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

    public DownloadableInfo Download { get; }
}

public class StoreVersion : IPlayableVersion
{
    private readonly string storeName;

    public StoreVersion(string storeName, string readableName)
    {
        this.storeName = readableName;
        InternalStoreName = storeName;
    }

    public string VersionName => storeName;

    public string FolderName => "./";

    public bool IsStoreVersion => true;

    public bool IsDevBuild => false;
    public bool IsPublicBuildA => false;
    public bool IsPublicBuildB => false;
    public bool IsPublicBuildC => false;

    public DownloadableInfo Download =>
        throw new InvalidOperationException("Store versions can't be separately downloaded");

    public string InternalStoreName { get; }
}

public class DevBuildVersion : IPlayableVersion
{
    private readonly PlayableDevCenterBuildType type;
    private readonly Lazy<Task<DownloadableInfo>> retrieveDownload;

    public DevBuildVersion(PlayableDevCenterBuildType type,
        Func<DevBuildLauncherDTO, Task<DownloadableInfo>> retrieveDownload)
    {
        this.type = type;
        this.retrieveDownload = new Lazy<Task<DownloadableInfo>>(() =>
            retrieveDownload(ExactBuild ??
                throw new InvalidOperationException($"{nameof(ExactBuild)} not set for DevBuild")));
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

    public DownloadableInfo Download => DownloadAsync.Result;

    /// <summary>
    ///   Gets the async download operation, recommended over using the blocking <see cref="Download"/> property
    /// </summary>
    public Task<DownloadableInfo> DownloadAsync => retrieveDownload.Value;

    public bool IsPublicBuild => IsPublicBuildA || IsPublicBuildB || IsPublicBuildC;

    public PlayableDevCenterBuildType BuildType => type;

    /// <summary>
    ///   The exact build to play, only set just before this version is played as this needs to be retrieved from a
    ///   remote server each time.
    /// </summary>
    public DevBuildLauncherDTO? ExactBuild { get; set; }
}