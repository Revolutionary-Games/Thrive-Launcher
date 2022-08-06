namespace LauncherBackend.Models;

public class StoreVersionInfo
{
    /// <summary>
    ///   Non-store version constructor
    /// </summary>
    private StoreVersionInfo()
    {
        IsStoreVersion = false;
    }

    private StoreVersionInfo(string name, string readableName)
    {
        IsStoreVersion = true;
        StoreName = name;
        StoreReadableName = readableName;
    }

    public static StoreVersionInfo Instance { get; } = DetectStoreVersion();

    public bool IsStoreVersion { get; }

    public string StoreName { get; } = string.Empty;

    public string StoreReadableName { get; } = string.Empty;

    public bool IsSteam => IsStoreVersion && StoreName == "steam";

    public bool ShouldPreventDefaultDevCenterVisibility => IsSteam;

    private static StoreVersionInfo DetectStoreVersion()
    {
        // TODO: store detection
        return new StoreVersionInfo();
    }
}
