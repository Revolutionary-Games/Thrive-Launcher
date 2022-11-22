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

    /// <summary>
    ///   Creates a store version from this info
    /// </summary>
    /// <param name="overriddenReadableName">
    ///   If not null overrides the store version. Note that played version remembering is based on this.
    /// </param>
    /// <returns>The created <see cref="StoreVersion"/> object</returns>
    public StoreVersion CreateStoreVersion(string? overriddenReadableName = null)
    {
        overriddenReadableName ??= $"{StoreReadableName} Version";

        return new StoreVersion(StoreName, overriddenReadableName);
    }
}
