namespace ThriveLauncher.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;

public class ExternalTools : IExternalTools
{
    private readonly ILogger<ExternalTools> logger;
    private readonly Lazy<string> basePathForTools;

    public ExternalTools(ILogger<ExternalTools> logger)
    {
        this.logger = logger;
        basePathForTools = new Lazy<string>(DetermineToolsPath);
    }

    private string BasePathForTools => basePathForTools.Value;

    public Task<bool> Run7Zip(string sourceArchive, string targetFolder, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    public Task<bool> RunGodotPckTool(string pckFile, IEnumerable<(string FilePath, string NameInPck)> filesToAdd,
        CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    private ProcessStartInfo Setup7ZipRunning()
    {
        string executableName;

        if (OperatingSystem.IsLinux())
        {
            executableName = "7za";
        }
        else if (OperatingSystem.IsWindows())
        {
            executableName = "7za.exe";
        }
        else if (OperatingSystem.IsMacOS())
        {
            executableName = "7za_mac";
        }
        else
        {
            throw new NotSupportedException("7-zip not configured to work on current platform");
        }

        var executable = Path.Join(BasePathForTools, "7zip", executableName);

        return new ProcessStartInfo(executable);
    }

    private ProcessStartInfo SetupPckToolRunning()
    {
        string executableName;

        if (OperatingSystem.IsLinux())
        {
            executableName = "godotpcktool";
        }
        else if (OperatingSystem.IsWindows())
        {
            executableName = "godotpcktool.exe";
        }
        else
        {
            // TODO: mac support
            throw new NotSupportedException("7-zip not configured to work on current platform");
        }

        var executable = Path.Join(BasePathForTools, "pck", executableName);

        return new ProcessStartInfo(executable);
    }

    private string DetermineToolsPath()
    {
        foreach (var potentialFolder in FoldersToLookForTools())
        {
            var folder = Path.Join(potentialFolder, LauncherConstants.ToolsFolderName);
            logger.LogDebug("Potential folder for tools: {Folder}", folder);

            if (Directory.Exists(folder))
            {
                logger.LogInformation("Found tools folder at: {Folder}", folder);
                return folder;
            }
        }

        throw new Exception("Cannot find tools folder that should be included with the launcher");
    }

    private IEnumerable<string> FoldersToLookForTools()
    {
        yield return AppDomain.CurrentDomain.BaseDirectory;
        yield return Environment.CurrentDirectory;

        // This might be the same as the first one, but having the location of the executing assembly here gives a
        // warning that it might not work in single file deploy mode
        yield return AppContext.BaseDirectory;
    }
}
