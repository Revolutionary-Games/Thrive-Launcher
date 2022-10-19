namespace LauncherBackend.Services;

/// <summary>
///   Access to some external tools the launcher needs
/// </summary>
public interface IExternalTools
{
    public Task<bool> Run7Zip(string sourceArchive, string targetFolder, CancellationToken cancellationToken);

    public Task<bool> RunGodotPckTool(string pckFile, IEnumerable<(string FilePath, string NameInPck)> filesToAdd,
        CancellationToken cancellationToken);
}
