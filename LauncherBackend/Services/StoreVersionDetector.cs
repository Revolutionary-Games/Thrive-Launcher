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
        // TODO: store detection
        logger.LogInformation("TODO: implement store version detection");

        return new StoreVersionInfo();
    }
}

public interface IStoreVersionDetector
{
    public StoreVersionInfo Detect();
}
