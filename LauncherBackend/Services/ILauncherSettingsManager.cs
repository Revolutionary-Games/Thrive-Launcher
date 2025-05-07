namespace LauncherBackend.Services;

using Models;

public interface ILauncherSettingsManager
{
    public LauncherSettings Settings { get; }

    /// <summary>
    ///   Old settings from launcher 1.x versions, used for migrating settings the first time launcher 2.x is used.
    /// </summary>
    public LauncherSettings? V1Settings { get; }

    /// <summary>
    ///   The version last selected by the user to play
    /// </summary>
    public string? RememberedVersion { get; set; }

    /// <summary>
    ///   Saves changed settings. Must be called after <see cref="Settings"/> is modified
    /// </summary>
    /// <returns>Task returning true on success</returns>
    public Task<bool> Save();

    /// <summary>
    ///   Resets all settings to default values
    /// </summary>
    public void Reset();

    public Task<bool> ImportOldSettings();
}
