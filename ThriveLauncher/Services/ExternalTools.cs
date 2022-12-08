namespace ThriveLauncher.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using SharedBase.Utilities;

public class ExternalTools : IExternalTools
{
    private const string Unix7ZipExecutableName = "7za";

    // TODO: add system tool checking if we want to support that (probably should be an option to turn on)
    private static readonly IReadOnlyList<string> Windows7ZipSystemPaths = new[]
    {
        "C:\\Program Files\\7-Zip\\7z.exe",
        "C:\\Program Files (x86)\\7-Zip\\7z.exe",
    };

    private readonly ILogger<ExternalTools> logger;
    private readonly Lazy<string> basePathForTools;

    public ExternalTools(ILogger<ExternalTools> logger)
    {
        this.logger = logger;
        basePathForTools = new Lazy<string>(DetermineToolsPath);
    }

    private string BasePathForTools => basePathForTools.Value;

    public async Task Run7Zip(string sourceArchive, string targetFolder, CancellationToken cancellationToken)
    {
        var startInfo = Setup7ZipRunning();
        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add(sourceArchive);

        // Overwrite all
        startInfo.ArgumentList.Add("-aoa");

        startInfo.ArgumentList.Add($"-O{targetFolder}");

        logger.LogDebug("Starting unpacking of {SourceArchive}", sourceArchive);
        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);

        logger.LogDebug("Unpacker exited with code: {ExitCode}", result.ExitCode);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Unpacking failed to run, exit code: {result.ExitCode}, output: {result.FullOutput.Truncate(300)}");
        }
    }

    public async Task RunGodotPckTool(string pckFile, IEnumerable<PckOperation> filesToAdd,
        CancellationToken cancellationToken)
    {
        var fileData = JsonSerializer.Serialize(filesToAdd);

        var startInfo = SetupPckToolRunning();
        startInfo.ArgumentList.Add(pckFile);
        startInfo.ArgumentList.Add("--action");
        startInfo.ArgumentList.Add("add");

        // Read from stdin
        startInfo.ArgumentList.Add("-");

        logger.LogTrace("Starting godotpcktool add operation with data: {FileData}", fileData);
        var output = new StringBuilder();

        void OnOutput(string line)
        {
            output.Append(line);
            output.Append("\n");
        }

        var result =
            await ProcessRunHelpers.RunProcessWithStdInAndOutputStreamingAsync(startInfo, cancellationToken,
                new[] { fileData }, OnOutput, OnOutput);

        logger.LogDebug("godotpcktool exited with code: {ExitCode}", result.ExitCode);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Godotpcktool .pck modification failed to run, exit code: {result.ExitCode}, " +
                $"output: {output.ToString().Truncate(300)}");
        }
    }

    private ProcessStartInfo Setup7ZipRunning()
    {
        string executableName;

        if (OperatingSystem.IsLinux())
        {
            executableName = Unix7ZipExecutableName;
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

        if (!File.Exists(executable))
            throw new Exception("7-zip is missing. It should have been included with the launcher");

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
            throw new NotSupportedException("Godotpcktool not configured to work on current platform");
        }

        var executable = Path.Join(BasePathForTools, "pck", executableName);

        if (!File.Exists(executable))
            throw new Exception("godotpcktool is missing. It should have been included with the launcher");

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
