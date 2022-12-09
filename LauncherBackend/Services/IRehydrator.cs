namespace LauncherBackend.Services;

using System.Collections.ObjectModel;
using Models;

/// <summary>
///   Handles rehydrating dehydrated Thrive folders
/// </summary>
public interface IRehydrator
{
    /// <summary>
    ///   Processes a dehydrated JSON info file to rehydrate the folder it is contained in
    /// </summary>
    /// <param name="dehydratedCacheFile">Path to the dehydrated cache</param>
    /// <param name="inProgressOperations">Where to show progress</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <exception cref="Exception">When rehydration fails (or a more derived and specific exception)</exception>
    public Task Rehydrate(string dehydratedCacheFile,
        ObservableCollection<FilePrepareProgress> inProgressOperations, CancellationToken cancellationToken);
}
