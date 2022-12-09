namespace LauncherBackend.Services;

using Models;

public interface IStoreVersionDetector
{
    /// <summary>
    ///   Detects the store version info. After first call returns a cached result.
    /// </summary>
    /// <returns>The calculated store version info</returns>
    public StoreVersionInfo Detect();
}
