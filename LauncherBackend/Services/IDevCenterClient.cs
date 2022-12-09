namespace LauncherBackend.Services;

using DevCenterCommunication.Models;
using Models;

/// <summary>
///   Manages connecting to the DevCenter and performing DevCenter operations
/// </summary>
public interface IDevCenterClient
{
    /// <summary>
    ///   The current DevCenter connection info
    /// </summary>
    public DevCenterConnection? DevCenterConnection { get; }

    public Task<DevCenterResult> CheckDevCenterConnection();

    /// <summary>
    ///   Retrieves info from the DevCenter about the build we want to play
    /// </summary>
    /// <returns>The build to play or null</returns>
    /// <exception cref="Exception">If manually selected hash is selected but none is set in settings</exception>
    public Task<DevBuildLauncherDTO?> FetchBuildWeWantToPlay();

    /// <summary>
    ///   Gets download info for a build
    /// </summary>
    /// <param name="build">The build to get a download for</param>
    /// <returns>The download info or null if there was an error retrieving the data</returns>
    public Task<DevBuildDownload?> GetDownloadForBuild(DevBuildLauncherDTO build);

    /// <summary>
    ///   Gets download urls for dehydrated objects based on on their hashes
    /// </summary>
    /// <param name="dehydratedObjects">The objects to ask downloads for</param>
    /// <returns>If successful list of downloads related to the requested hashes</returns>
    public Task<List<DehydratedObjectDownloads.DehydratedObjectDownload>?> GetDownloadsForDehydrated(
        IEnumerable<DehydratedObjectIdentification> dehydratedObjects);

    /// <summary>
    ///   Queries the DevCenter for the latest builds
    /// </summary>
    /// <param name="offset">Offset to use (start at 0)</param>
    /// <param name="pageSize">The page size to fetch at once</param>
    /// <returns>The found latest builds or null on error</returns>
    public Task<List<DevBuildLauncherDTO>?> FetchLatestBuilds(int offset, int pageSize = 75);

    public Task<DevCenterResult> FormConnection(string code);

    public Task<(LauncherConnectionStatus? Status, string? Error)> CheckLinkCode(string code);

    public Task Logout();

    public Task ClearTokenInSettings();
}
