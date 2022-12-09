namespace LauncherBackend.Services;

using DevCenterCommunication.Models;

public interface IThriveAndLauncherInfoRetriever
{
    public bool IgnoreSigningRequirement { get; set; }

    public LauncherThriveInformation? CurrentlyLoadedInfo { get; }

    public Task<LauncherThriveInformation> DownloadInfo();

    public bool HasCachedFile();

    public Task<LauncherThriveInformation?> LoadFromCache();

    /// <summary>
    ///   Re-enables info stored by <see cref="DownloadInfo"/> into <see cref="CurrentlyLoadedInfo"/>
    /// </summary>
    public void RestoreBackupInfo();

    /// <summary>
    ///   Forgets the currently loaded info in <see cref="CurrentlyLoadedInfo"/>. This is used for disabling external
    ///   versions in store versions.
    /// </summary>
    public void ForgetInfo();
}
