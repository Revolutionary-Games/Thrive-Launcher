namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using Models;
using SharedBase.Models;

public interface IThriveInstaller
{
    /// <summary>
    ///   Messages from the current install process
    /// </summary>
    public ObservableCollection<ThrivePlayMessage> InstallerMessages { get; }

    /// <summary>
    ///   Progress reporting for the current installation process
    /// </summary>
    public ObservableCollection<FilePrepareProgress> InProgressOperations { get; }

    public string BaseInstallFolder { get; }

    public IEnumerable<(string VersionName, IPlayableVersion VersionObject)> GetAvailableThriveVersions();

    /// <summary>
    ///   Sorts versions to be in the order they should be shown to the user
    /// </summary>
    /// <param name="versions">The versions to sort</param>
    /// <returns>Versions in sorted order</returns>
    public IOrderedEnumerable<(string VersionName, IPlayableVersion VersionObject)> SortVersions(
        IEnumerable<(string VersionName, IPlayableVersion VersionObject)> versions);

    public IEnumerable<string> DetectInstalledThriveFolders();

    public IEnumerable<FolderInInstallFolder> ListFoldersInThriveInstallFolder();

    /// <summary>
    ///   Lists all files in the temporary folder, even non-Thrive related
    /// </summary>
    /// <returns>Enumerable of the files</returns>
    public IEnumerable<string> ListFilesInTemporaryFolder();

    /// <summary>
    ///   Lists all files in the dehydrate cache folder, even non-Thrive related (if such files are put there)
    /// </summary>
    /// <returns>Enumerable of the files</returns>
    public IEnumerable<string> ListFilesInDehydrateCache();

    /// <summary>
    ///   Makes sure the version is installed, if not starts the whole process of downloading it.
    /// </summary>
    /// <param name="playableVersion">The version to check</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Task resulting in true when everything is fine</returns>
    public Task<bool> EnsureVersionIsDownloaded(IPlayableVersion playableVersion, CancellationToken cancellationToken);

    /// <summary>
    ///   Looks for the bin folder (old releases) or the folder containing the Thrive executable folder
    /// </summary>
    /// <param name="installedThriveFolder">The base folder of the installed version to start looking for</param>
    /// <param name="platform">Which platform this installation is for (used to know the executable name)</param>
    /// <param name="fallback">
    ///   If true, then the last found folder is returned, even if no bin folder is found. Should always be true on the
    ///   top level call to find the folder.
    /// </param>
    /// <returns>The found folder with the Thrive executable or null if not found</returns>
    public string? FindThriveExecutableFolderInVersion(string installedThriveFolder, PackagePlatform platform,
        bool fallback = true);

    public bool ThriveExecutableExistsInFolder(string folder, PackagePlatform platform);
}
