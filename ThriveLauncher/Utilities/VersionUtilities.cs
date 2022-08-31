namespace ThriveLauncher.Utilities;

using System;
using System.Reflection;
using Microsoft.Extensions.Logging;

public class VersionUtilities
{
    public VersionUtilities(ILogger<VersionUtilities> logger)
    {
        try
        {
            LauncherVersion = GetCurrentVersion();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting assembly version");
            LauncherVersion = "Error";
        }
    }

    public VersionUtilities()
    {
        LauncherVersion = GetCurrentVersion();
    }

    public string LauncherVersion { get; }

    private string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ??
            throw new Exception("Version for assembly doesn't exist");

        if (version.Build == 0)
            return $"{version.Major}.{version.Minor}.{version.Revision}";

        return $"{version.Major}.{version.Minor}.{version.Revision}-{version.Build}";
    }
}
