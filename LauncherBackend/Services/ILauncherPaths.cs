namespace LauncherBackend.Services;

public interface ILauncherPaths
{
    public string PathToSettings { get; }

    public string PathToRememberedVersion { get; }

    public string PathToAutoUpdateFile { get; }

    public string PathToLauncherConfig { get; }

    public string PathToDefaultThriveInstallFolder { get; }
    public string PathToCachedDownloadedLauncherInfo { get; }

    public string PathToDefaultDehydrateCacheFolder { get; }

    public string PathToTemporaryFolder { get; }
    public string PathToLogFolder { get; }

    public string ThriveDefaultLogsFolder { get; }
    public string ThriveDefaultCrashesFolder { get; }
    public string ThriveDefaultStartUpFile { get; }

    // Launcher 1.x version folder paths

    public string PathToSettingsV1 { get; }

    public string PathToLauncherV1Config { get; }

    public string PathToDefaultThriveInstallFolderV1 { get; }
    public string PathToDefaultDehydrateCacheFolderV1 { get; }
}
