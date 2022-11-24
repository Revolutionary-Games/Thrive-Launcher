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
            AssemblyVersion = GetAssemblyVersion();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting assembly version");
            LauncherVersion = "Error";
            AssemblyVersion = new Version(0, 0, 0);
        }
    }

    public VersionUtilities()
    {
        LauncherVersion = GetCurrentVersion();
        AssemblyVersion = GetAssemblyVersion();
    }

    public string LauncherVersion { get; }

    public Version AssemblyVersion { get; }

    private string GetCurrentVersion()
    {
        var version = GetAssemblyVersion();

        if (version.Revision == 0)
            return $"{version.Major}.{version.Minor}.{version.Build}";

        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private Version GetAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ??
            throw new Exception("Version for assembly doesn't exist");
    }
}
