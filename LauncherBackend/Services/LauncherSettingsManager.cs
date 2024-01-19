namespace LauncherBackend.Services;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Models;

public class LauncherSettingsManager : ILauncherSettingsManager
{
    private readonly ILogger<LauncherSettingsManager> logger;
    private readonly ILauncherPaths launcherPaths;
    private readonly IBackgroundExceptionHandler backgroundExceptionHandler;

    private readonly Lazy<string?> rememberedVersion;

    /// <summary>
    ///   Runtime overridden version of the lazy readonly field <see cref="rememberedVersion"/>. This uses a separate
    ///   class to hold a single string to differentiate between no overridden value and the value being null.
    /// </summary>
    private OverriddenVersion? overriddenRememberedVersion;

    private Lazy<LauncherSettings> settings;

    private LauncherSettings? v1Settings;

    private bool settingsLoaded;

    public LauncherSettingsManager(ILogger<LauncherSettingsManager> logger, ILauncherPaths launcherPaths,
        IBackgroundExceptionHandler backgroundExceptionHandler)
    {
        this.logger = logger;
        this.launcherPaths = launcherPaths;
        this.backgroundExceptionHandler = backgroundExceptionHandler;

        settings = new Lazy<LauncherSettings>(() => AttemptToLoadSettings() ?? new LauncherSettings());
        rememberedVersion = new Lazy<string?>(LoadRememberedVersion);
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

    public string? RememberedVersion
    {
        get => overriddenRememberedVersion?.OverriddenValue ?? rememberedVersion.Value;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;

            if (overriddenRememberedVersion != null && overriddenRememberedVersion.OverriddenValue == value)
                return;

            overriddenRememberedVersion ??= new OverriddenVersion();

            overriddenRememberedVersion.OverriddenValue = value;

            backgroundExceptionHandler.HandleTask(SaveRememberedVersion(value));
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

    public void Reset()
    {
        settings = new Lazy<LauncherSettings>(new LauncherSettings());
    }

    public Task<bool> ImportOldSettings()
    {
        var old = v1Settings;

        if (old == null)
            throw new InvalidOperationException("Old settings have not been detected");

        var target = Settings;

        // This list doesn't need to be maintained as the old launcher won't gain new settings
        target.ShowWebContent = old.ShowWebContent;
        target.Hide32Bit = old.Hide32Bit;
        target.CloseLauncherAfterGameExit = old.CloseLauncherAfterGameExit;
        target.CloseLauncherOnGameStart = old.CloseLauncherOnGameStart;
        target.StoreVersionShowExternalVersions = old.StoreVersionShowExternalVersions;
        target.BeginningKeptGameOutput = old.BeginningKeptGameOutput;
        target.LastKeptGameOutput = old.LastKeptGameOutput;
        target.ThriveInstallationPath = old.ThriveInstallationPath;
        target.DehydratedCacheFolder = old.DehydratedCacheFolder;
        target.DevCenterKey = old.DevCenterKey;
        target.SelectedDevBuildType = old.SelectedDevBuildType;
        target.ManuallySelectedBuildHash = old.ManuallySelectedBuildHash;
        target.ForceGles2Mode = old.ForceGles2Mode;
        target.DisableThriveVideos = old.DisableThriveVideos;

        // Temporary downloads removed from here as the setting was updated to mean something else
        // Hide on play removed from here as the defaults were changed

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

    private async Task SaveRememberedVersion(string? version)
    {
        if (version == null)
        {
            if (File.Exists(launcherPaths.PathToRememberedVersion))
                File.Delete(launcherPaths.PathToRememberedVersion);

            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPaths.PathToRememberedVersion) ??
                throw new Exception("Remembered version path has no folder"));
            await File.WriteAllTextAsync(launcherPaths.PathToRememberedVersion, version, Encoding.UTF8);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save remembered version");
        }
    }

    private string? LoadRememberedVersion()
    {
        if (!File.Exists(launcherPaths.PathToRememberedVersion))
            return null;

        try
        {
            var text = File.ReadAllText(launcherPaths.PathToRememberedVersion, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load remembered version");
            return null;
        }
    }

    private class OverriddenVersion
    {
        public string? OverriddenValue;
    }
}
