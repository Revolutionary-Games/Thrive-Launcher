namespace LauncherBackend.Services;

using LauncherThriveShared;
using Microsoft.Extensions.Logging;

/// <summary>
///   Methods for getting launcher paths for the current platform
/// </summary>
public class LauncherPaths : ILauncherPaths
{
    private const string LauncherConfigFolderName = "Thrive-Launcher";
    private const string LauncherTemporaryFolderName = "temp";
    private const string ThriveUserDataFolderName = "Thrive";

    private static readonly string SettingsFileName = $"launcher_settings{LauncherConstants.ModeSuffix}.json";

    private static readonly string RememberedVersionFileName =
        $"selected_version_v2{LauncherConstants.ModeSuffix}.json";

    private static readonly string LauncherV1SettingsFileName = "launcher_settings.json";

    private static readonly string AutoUpdateFileName = "attempted_auto_update.json";

    private readonly ILogger<LauncherPaths> logger;

    private string? configFolder;
    private string? fileInstallFolder;
    private string? electronConfigFolder;
    private string? thriveUserFolder;
    private string? tempPath;

    public LauncherPaths(ILogger<LauncherPaths> logger)
    {
        this.logger = logger;
    }

    public string PathToSettings => Path.Combine(PathToLauncherConfig, SettingsFileName);

    public string PathToRememberedVersion => Path.Combine(PathToLauncherConfig, RememberedVersionFileName);

    public string PathToAutoUpdateFile => Path.Combine(PathToLauncherConfig, AutoUpdateFileName);

    public string PathToLauncherConfig => configFolder ??= GetPlatformConfigFolder();
    public string PathToLauncherInstallBaseFolder => fileInstallFolder ??= GetPlatformInstallFolder();

    public string PathToCachedDownloadedLauncherInfo =>
        Path.Combine(PathToLauncherInstallBaseFolder, "cached_version_info.bin");

    public string PathToDefaultThriveInstallFolder => Path.Combine(PathToLauncherInstallBaseFolder, "installed");

    public string PathToDefaultDehydrateCacheFolder =>
        Path.Combine(PathToLauncherInstallBaseFolder, "dehydrated_cache");

    public string PathToTemporaryFolder => tempPath ??= GetTemporaryPath();

    public string PathToLogFolder => Path.Combine(PathToLauncherInstallBaseFolder, "logs");

    public string ExpectedDefaultThriveUserFolder => thriveUserFolder ??= GetPlatformExpectedThriveUserFolder();

    public string ThriveDefaultLogsFolder =>
        Path.Combine(ExpectedDefaultThriveUserFolder, ThriveLauncherSharedConstants.LOGS_FOLDER_NAME);

    public string ThriveDefaultCrashesFolder =>
        Path.Combine(ExpectedDefaultThriveUserFolder, LauncherConstants.ThriveCrashesFolderName);

    // Launcher 1.x version folder paths

    public string PathToSettingsV1 => Path.Combine(PathToLauncherV1Config, LauncherV1SettingsFileName);

    public string PathToLauncherV1Config => electronConfigFolder ??= GetElectronCompatibleAppDataFolder();

    public string PathToDefaultThriveInstallFolderV1 => Path.Combine(PathToLauncherV1Config, "Installed");
    public string PathToDefaultDehydrateCacheFolderV1 => Path.Combine(PathToLauncherV1Config, "DehydratedCache");

    private static string GetXDGConfigHome()
    {
        var path = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        if (!string.IsNullOrEmpty(path))
            return path;

        return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static string GetXDGDataHome()
    {
        var path = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (!string.IsNullOrEmpty(path))
            return path;

        return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static string GetEnvironmentFolderOrFallback(Environment.SpecialFolder specialFolder)
    {
        var folder = Environment.GetFolderPath(specialFolder);

        if (!string.IsNullOrWhiteSpace(folder) && folder != "/")
            return folder;

        // Older macs (Intel based) seem to mostly be in need of this fallback
        if (OperatingSystem.IsMacOS())
        {
            if (specialFolder == Environment.SpecialFolder.UserProfile)
            {
                return $"/Users/{Environment.UserName}";
            }

            return Path.Join(GetEnvironmentFolderOrFallback(Environment.SpecialFolder.UserProfile), "Library",
                "Application Support");
        }

        if (OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Fallback shouldn't be necessary on Windows");
        }

        return GetXDGConfigHome();
    }

    private string GetPlatformConfigFolder()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LauncherConfigFolderName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            path = Path.Join(GetEnvironmentFolderOrFallback(Environment.SpecialFolder.ApplicationData),
                LauncherConfigFolderName);
        }
        else
        {
            if (!OperatingSystem.IsLinux())
            {
                logger.LogError("Unknown operating system for config folder finding");
            }

            path = Path.Combine(GetXDGConfigHome(), LauncherConfigFolderName);
        }

        logger.LogInformation("Config folder is: {Path}", path);
        return path;
    }

    private string GetPlatformInstallFolder()
    {
        string path;

        if (OperatingSystem.IsLinux())
        {
            path = Path.Combine(GetXDGDataHome(), LauncherConfigFolderName);
        }
        else
        {
            path = Path.Join(GetEnvironmentFolderOrFallback(Environment.SpecialFolder.ApplicationData),
                LauncherConfigFolderName);
        }

        logger.LogInformation("Default Launcher storage and Thrive version install folder is: {Path}", path);
        return path;
    }

    private string GetPlatformExpectedThriveUserFolder()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ThriveUserDataFolderName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            path = Path.Join(GetEnvironmentFolderOrFallback(Environment.SpecialFolder.ApplicationData),
                ThriveUserDataFolderName);
        }
        else
        {
            if (!OperatingSystem.IsLinux())
            {
                logger.LogError("Unknown operating system for config folder finding");
            }

            path = Path.Combine(GetXDGDataHome(), ThriveUserDataFolderName);
        }

        logger.LogInformation("Expected Thrive user data folder to be: {Path}", path);
        return path;
    }

    private string GetElectronCompatibleAppDataFolder()
    {
        string path;

        if (OperatingSystem.IsWindows())
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            path = Path.Join(GetEnvironmentFolderOrFallback(Environment.SpecialFolder.UserProfile), "Library",
                "Application Support");
        }
        else
        {
            if (!OperatingSystem.IsLinux())
            {
                logger.LogError("Unknown operating system for Electron compatible data folder detection");

                // Linux path is the default if we don't know the OS so we don't error here
            }

            path = GetXDGConfigHome();
        }

        path = Path.Combine(path, "Revolutionary-Games", "Launcher");

        logger.LogInformation("Expected old launcher config path is: {Path}", path);
        return path;
    }

    private string GetTemporaryPath()
    {
        var path = Path.Combine(PathToLauncherInstallBaseFolder, LauncherTemporaryFolderName);

        logger.LogInformation("Temporary folder is: {Path}", path);
        return path;
    }
}
