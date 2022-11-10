namespace LauncherBackend.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Models;

/// <summary>
///   Access to some external tools the launcher needs
/// </summary>
public interface IExternalTools
{
    public Task Run7Zip(string sourceArchive, string targetFolder, CancellationToken cancellationToken);

    public Task RunGodotPckTool(string pckFile, IEnumerable<PckOperation> filesToAdd,
        CancellationToken cancellationToken);
}
