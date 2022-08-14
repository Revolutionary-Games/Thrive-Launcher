using System.Text;
using System.Text.Json;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;

namespace LauncherBackend.Services;

public class LauncherSettingsManager : ILauncherSettingsManager
{
    private readonly ILogger<LauncherSettingsManager> logger;
    private readonly ILauncherPaths launcherPaths;
    private readonly Lazy<LauncherSettings> settings;

    private LauncherSettings? v1Settings;

    private bool settingsLoaded;

    public LauncherSettingsManager(ILogger<LauncherSettingsManager> logger, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.launcherPaths = launcherPaths;

        settings = new Lazy<LauncherSettings>(() => AttemptToLoadSettings() ?? new LauncherSettings());
    }

    public LauncherSettings Settings => settings.Value;

    public LauncherSettings? V1Settings
    {
        get
        {
            if (!settingsLoaded)
                _ = settings.Value;

            return v1Settings;
        }
    }

    public async Task<bool> Save()
    {
        var folder = Path.GetDirectoryName(launcherPaths.PathToSettings);

        if (folder != null)
        {
            try
            {
                if (!Directory.CreateDirectory(folder).Exists)
                    throw new IOException("Failed to create directory");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create folder for settings file");
                return false;
            }
        }

        try
        {
            await File.WriteAllTextAsync(launcherPaths.PathToSettings, JsonSerializer.Serialize(Settings),
                Encoding.UTF8);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to write current settings to file");
            return false;
        }

        return true;
    }

    public Task<bool> ImportOldSettings()
    {
        var old = v1Settings;

        if (old == null)
            throw new InvalidOperationException("Old settings have not been detected");

        var target = Settings;

        // This list doesn't need to be maintained as the old launcher won't gain new settings
        target.ShowWebContent = old.ShowWebContent;
        target.HideLauncherOnPlay = old.HideLauncherOnPlay;
        target.Hide32Bit = old.Hide32Bit;
        target.CloseLauncherAfterGameExit = old.CloseLauncherAfterGameExit;
        target.CloseLauncherOnGameStart = old.CloseLauncherOnGameStart;
        target.StoreVersionShowExternalVersions = old.StoreVersionShowExternalVersions;
        target.AutoStartStoreVersion = old.AutoStartStoreVersion;
        target.BeginningKeptGameOutput = old.BeginningKeptGameOutput;
        target.LastKeptGameOutput = old.LastKeptGameOutput;
        target.ThriveInstallationPath = old.ThriveInstallationPath;
        target.DehydratedCacheFolder = old.DehydratedCacheFolder;
        target.TemporaryDownloadsFolder = old.TemporaryDownloadsFolder;
        target.DevCenterKey = old.DevCenterKey;
        target.SelectedDevBuildType = old.SelectedDevBuildType;
        target.ManuallySelectedBuildHash = old.ManuallySelectedBuildHash;
        target.ForceGles2Mode = old.ForceGles2Mode;
        target.DisableThriveVideos = old.DisableThriveVideos;

        return Save();
    }

    private LauncherSettings? AttemptToLoadSettings()
    {
        settingsLoaded = true;

        if (File.Exists(launcherPaths.PathToSettings))
        {
            try
            {
                var settingsText = File.ReadAllText(launcherPaths.PathToSettings, Encoding.UTF8);

                var deserialized = JsonSerializer.Deserialize<LauncherSettings>(settingsText);

                if (deserialized != null)
                {
                    logger.LogInformation("Loaded settings from {PathToSettings}", launcherPaths.PathToSettings);
                }

                return deserialized;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to load settings");
            }
        }

        logger.LogInformation("No existing settings file found, using defaults");

        // Skip detecting old settings if their path would be the same
        if (Path.GetFullPath(launcherPaths.PathToSettingsV1) == Path.GetFullPath(launcherPaths.PathToSettings))
            return null;

        // Try to detect old launcher settings to allow settings migration
        if (!File.Exists(launcherPaths.PathToSettingsV1))
        {
            // Old settings don't exist
            return null;
        }

        try
        {
            var settingsText = File.ReadAllText(launcherPaths.PathToSettingsV1, Encoding.UTF8);

            v1Settings = JsonSerializer.Deserialize<LauncherSettings>(settingsText);

            if (v1Settings != null)
            {
                logger.LogInformation("Detected old launcher settings at {PathToSettings}",
                    launcherPaths.PathToSettingsV1);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to load old settings (for settings migration support)");
        }

        return null;
    }
}

public interface ILauncherSettingsManager
{
    public LauncherSettings Settings { get; }

    /// <summary>
    ///   Old settings from launcher 1.x versions, used for migrating settings first time launcher 2.x is used.
    /// </summary>
    public LauncherSettings? V1Settings { get; }

    /// <summary>
    ///   Saves changed settings. Must be called after <see cref="Settings"/> is modified
    /// </summary>
    /// <returns>Task returning true on success</returns>
    public Task<bool> Save();

    Task<bool> ImportOldSettings();
}
