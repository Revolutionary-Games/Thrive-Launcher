// #define INTENTIONALLY_FAIL_CPU_CHECK

namespace LauncherBackend.Services;

using System.Runtime.Intrinsics.X86;
using Microsoft.Extensions.Logging;

public interface ICPUFeatureCheck
{
    public bool IsBasicThriveLibrarySupported();
}

public class CPUFeatureCheck : ICPUFeatureCheck
{
    private readonly ILogger<CPUFeatureCheck> logger;

    private readonly Lazy<bool> isSupported;

    public CPUFeatureCheck(ILogger<CPUFeatureCheck> logger)
    {
        this.logger = logger;

        isSupported = new Lazy<bool>(CheckFeatures);
    }

    public bool IsBasicThriveLibrarySupported()
    {
        return isSupported.Value;
    }

    private bool CheckFeatures()
    {
        if (!Sse41.IsSupported)
        {
            logger.LogWarning("CPU is detected as not having SSE 4.1 support");
            return false;
        }

        if (!Sse42.IsSupported)
        {
            logger.LogWarning("CPU is detected as not having SSE 4.2 support");
            return false;
        }

#if INTENTIONALLY_FAIL_CPU_CHECK
        logger.LogWarning("Intentionally failing CPU check for debugging purposes");
        return false;
#else
        return true;
#endif
    }
}
