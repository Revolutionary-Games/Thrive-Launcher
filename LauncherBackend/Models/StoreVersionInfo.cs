namespace LauncherBackend.Models;

public class StoreVersionInfo
{
    public const string SteamInternalName = "steam";

    /// <summary>
    ///   Non-store version constructor
    /// </summary>
    public StoreVersionInfo()
    {
        IsStoreVersion = false;
    }

    public StoreVersionInfo(string name, string readableName, string storePageUrl)
    {
        IsStoreVersion = true;
        StoreName = name;
        StoreReadableName = readableName;
        StorePageUrl = storePageUrl;
    }

    public bool IsStoreVersion { get; }

    public string StoreName { get; } = string.Empty;
    public string StoreReadableName { get; } = string.Empty;

    public string StorePageUrl { get; } = string.Empty;

    public bool IsSteam => IsStoreVersion && StoreName == SteamInternalName;

    public bool ShouldPreventDefaultDevCenterVisibility => IsSteam;
}