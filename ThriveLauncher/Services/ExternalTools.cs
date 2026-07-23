namespace ThriveLauncher.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GodotPckTool;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using SharedBase.Utilities;

public class ExternalTools : IExternalTools
{
    private const string Unix7ZipExecutableName = "7zz";

    private static readonly string[] Windows7ZipSystemPaths =
    [
        "C:\\Program Files\\7-Zip\\7z.exe",
        "C:\\Program Files (x86)\\7-Zip\\7z.exe",
    ];

    private readonly ILogger<ExternalTools> logger;
    private readonly ILauncherSettingsManager settings;
    private readonly Lazy<string> basePathForTools;

    public ExternalTools(ILogger<ExternalTools> logger, ILauncherSettingsManager settings)
    {
        this.logger = logger;
        this.settings = settings;
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

    /// <summary>
    ///   Run a Godot PCK tool operation to add files. Note that this is now implemented as a C# module.
    /// </summary>
    /// <param name="pckFile">.pck file to process</param>
    /// <param name="filesToAdd">Files to add</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <exception cref="Exception">Thrown if this cannot update the PCK file</exception>
    public async Task RunGodotPckTool(string pckFile, IEnumerable<PckOperation> filesToAdd,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            using var pck = new PckFile(pckFile);

            if (File.Exists(pckFile))
            {
                if (!pck.Load())
                    throw new Exception($"Failed to load existing PCK file: {pckFile}");
            }

            foreach (var operation in filesToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pck.AddSingleFile(operation.FilePath, operation.TargetNameInPck);
            }

            if (!pck.Save())
                throw new Exception($"Failed to save PCK file: {pckFile}");
        }, cancellationToken);
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
            executableName = "7zz_mac";
        }
        else
        {
            throw new NotSupportedException("7-zip not configured to work on current platform");
        }

        if (settings.Settings.PreferSystemTools)
        {
            var fromPath = ExecutableFinder.Which(executableName);

            if (string.IsNullOrEmpty(fromPath) && OperatingSystem.IsMacOS())
            {
                fromPath = ExecutableFinder.Which(Unix7ZipExecutableName);
            }

            if (string.IsNullOrEmpty(fromPath) && !OperatingSystem.IsWindows())
            {
                // Fallback to the old name of the tool (p7zip)
                fromPath = ExecutableFinder.Which("7za");
            }

            if (!string.IsNullOrEmpty(fromPath) && File.Exists(fromPath))
            {
                logger.LogInformation("Using 7-zip from PATH: {Path}", fromPath);
                return new ProcessStartInfo(fromPath);
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (var path in Windows7ZipSystemPaths)
                {
                    if (File.Exists(path))
                    {
                        logger.LogInformation("Using system-installed 7-zip from: {Path}", path);
                        return new ProcessStartInfo(path);
                    }
                }
            }
        }

        var executable = Path.Join(BasePathForTools, "7zip", executableName);

        if (!File.Exists(executable))
            throw new Exception("7-zip is missing. It should have been included with the launcher");

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
