namespace LauncherBackend.Services;

using Microsoft.Extensions.Logging;
using Models;

public class StoreVersionDetector : IStoreVersionDetector
{
    private readonly ILogger<StoreVersionDetector> logger;
    private readonly Lazy<StoreVersionInfo> detectedInfo;

    public StoreVersionDetector(ILogger<StoreVersionDetector> logger)
    {
        this.logger = logger;
        detectedInfo = new Lazy<StoreVersionInfo>(DetectStoreVersion);
    }

    public StoreVersionInfo Detect()
    {
        return detectedInfo.Value;
    }

    private StoreVersionInfo DetectStoreVersion()
    {
        // TODO: check that this approach works
#if LAUNCHER_STEAM
        logger.LogInformation("This is a Steam version of the launcher");
        return new StoreVersionInfo(StoreVersionInfo.SteamInternalName, "Steam", LauncherConstants.ThriveSteamURL);
#elif LAUNCHER_ITCH
        logger.LogInformation("This is an itch.io version of the launcher");
        return new StoreVersionInfo("itch", "Itch.io", LauncherConstants.ThriveItchURL);
#endif

        logger.LogInformation("Not a store version of the launcher");
        return new StoreVersionInfo();
    }
}

public interface IStoreVersionDetector
{
    /// <summary>
    ///   Detects the store version info. After first call returns a cached result.
    /// </summary>
    /// <returns>The calculated store version info</returns>
    public StoreVersionInfo Detect();
}
