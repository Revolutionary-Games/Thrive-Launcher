using Microsoft.Extensions.Logging;

namespace LauncherBackend.Services;

/// <summary>
///   Methods for getting launcher paths for the current platform
/// </summary>
public class LauncherPaths : ILauncherPaths
{
    public const string SettingsFileName = "launcher_settings.json";
    public const string RememberedVersionFileName = "selected_version_v2.json";
    public const string InstalledDevBuildFolderName = "devbuild";

    public const string LauncherConfigFolderName = "Thrive-Launcher";
    public const string LauncherTemporaryFolderName = "Revolutionary-Games-Launcher";
    public const string ThriveUserDataFolderName = "Thrive";

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

    public string PathToLauncherConfig => configFolder ??= GetPlatformConfigFolder();
    public string PathToLauncherInstallBaseFolder => fileInstallFolder ??= GetPlatformInstallFolder();

    public string PathToDefaultThriveInstallFolder => Path.Combine(PathToLauncherInstallBaseFolder, "installed");

    public string PathToDefaultDehydrateCacheFolder =>
        Path.Combine(PathToLauncherInstallBaseFolder, "dehydrated_cache");

    public string PathToTemporaryFolder => tempPath ??= GetTemporaryPath();

    public string ExpectedDefaultThriveUserFolder => thriveUserFolder ??= GetPlatformExpectedThriveUserFolder();

    public string ThriveDefaultLogsFolder => Path.Combine(ExpectedDefaultThriveUserFolder, "logs");
    public string ThriveDefaultCrashesFolder => Path.Combine(ExpectedDefaultThriveUserFolder, "crashes");

    // Launcher 1.x version folder paths

    public string PathToSettingsV1 => Path.Combine(PathToLauncherV1Config, SettingsFileName);

    public string PathToLauncherV1Config => electronConfigFolder ??= GetElectronCompatibleAppDataFolder();

    public string PathToDefaultThriveInstallFolderV1 => Path.Combine(PathToLauncherV1Config, "Installed");
    public string PathToDefaultDehydrateCacheFolderV1 => Path.Combine(PathToLauncherV1Config, "DehydratedCache");

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
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LauncherConfigFolderName);
        }

        logger.LogInformation("Default Thrive version install folder is: {Path}", path);
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
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library",
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
        var path = Path.Combine(Path.GetTempPath(), LauncherTemporaryFolderName);

        logger.LogInformation("Temporary folder is: {Path}", path);
        return path;
    }

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
}

public interface ILauncherPaths
{
    public string PathToSettings { get; }

    public string PathToRememberedVersion { get; }

    public string PathToLauncherConfig { get; }

    public string PathToDefaultThriveInstallFolder { get; }
    public string PathToDefaultDehydrateCacheFolder { get; }
    public string PathToTemporaryFolder { get; }

    // Launcher 1.x version folder paths

    public string PathToSettingsV1 { get; }

    public string PathToLauncherV1Config { get; }

    public string PathToDefaultThriveInstallFolderV1 { get; }
    public string PathToDefaultDehydrateCacheFolderV1 { get; }
}
