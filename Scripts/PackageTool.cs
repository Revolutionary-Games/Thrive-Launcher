namespace Scripts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScriptsBase.Models;
using ScriptsBase.ToolBases;
using ScriptsBase.Utilities;
using SharedBase.Models;
using SharedBase.Utilities;

public class PackageTool : PackageToolBase<Program.PackageOptions>
{
    private const string BuilderImageName = "localhost/thrive/launcher-builder:latest";

    private static readonly IReadOnlyList<PackagePlatform> LauncherPlatforms = new List<PackagePlatform>
    {
        PackagePlatform.Linux,
        PackagePlatform.Windows,
        PackagePlatform.Mac,
    };

    private static readonly IReadOnlyCollection<FileToPackage> ExtraFilesToPackage = new List<FileToPackage>
    {
        new("LICENSE.md", "LICENSE.md"),
        new("ThriveLauncher/ThriveLauncher.desktop", "ThriveLauncher.desktop", PackagePlatform.Linux),
        new("ThriveLauncher/launcher-icon.png", "thrive-launcher-icon.png", PackagePlatform.Linux),
    };

    private static readonly IReadOnlyCollection<string> SourceItemsToPackage = new List<string>
    {
        "ThriveLauncher.sln",
        "ThriveLauncher.sln.DotSettings",
        "ThriveLauncher",
        "Tests",
        "LauncherBackend",
        "tools",
        "LICENSE.md",
        "README.md",
        "RevolutionaryGamesCommon",
    };

    private readonly string launcherVersion;

    public PackageTool(Program.PackageOptions options) : base(options)
    {
        // Retries don't really work for us so set it to 0
        options.Retries = 0;

        // Mac builds need to be done on a mac
        if (OperatingSystem.IsMacOS())
        {
            DefaultPlatforms = new[] { PackagePlatform.Mac };
        }
        else
        {
            DefaultPlatforms = LauncherPlatforms.Where(p => p != PackagePlatform.Mac).ToList();
        }

        launcherVersion = AssemblyInfoReader.ReadVersionFromCsproj("ThriveLauncher/ThriveLauncher.csproj");
    }

    protected override IReadOnlyCollection<PackagePlatform> ValidPlatforms => LauncherPlatforms;

    protected override IEnumerable<PackagePlatform> DefaultPlatforms { get; }

    protected override IEnumerable<string> SourceFilesToPackage => SourceItemsToPackage;

    private string ReadmeFile => Path.Join(options.OutputFolder, "README.txt");
    private string RevisionFile => Path.Join(options.OutputFolder, "revision.txt");

    protected override async Task<bool> OnBeforeStartExport(CancellationToken cancellationToken)
    {
        if (options.LinuxPodman == true)
        {
            ColourConsole.WriteNormalLine("Podman will be used for Linux builds");
        }

        if (options.CreateDynamicFiles == true)
        {
            await CreateDynamicallyGeneratedFiles(cancellationToken);
        }
        else
        {
            ColourConsole.WriteWarningLine("Skipping dynamic file generation");
        }

        return true;
    }

    protected override string GetFolderNameForExport(PackagePlatform platform)
    {
        return ThriveProperties.GetFolderNameForLauncher(platform, launcherVersion);
    }

    protected override string GetCompressedExtensionForPlatform(PackagePlatform platform)
    {
        return $"_standalone{base.GetCompressedExtensionForPlatform(platform)}";
    }

    protected override async Task<bool> Export(PackagePlatform platform, string folder,
        CancellationToken cancellationToken)
    {
        ColourConsole.WriteInfoLine($"Starting dotnet publish for platform: {platform}");
        Directory.CreateDirectory(folder);

        if (platform == PackagePlatform.Mac)
        {
            // TODO: do the two builds and combine them (as there doesn't seem to be a combined way)
            // dotnet publish -c Release -r osx-x64 --self-contained true -o dist/mac-x64 ThriveLauncher
            // dotnet publish -c Release -r osx-arm64 --self-contained true -o dist/mac-arm ThriveLauncher

            throw new NotImplementedException();
        }
        else if (platform == PackagePlatform.Linux && options.LinuxPodman == true)
        {
            ColourConsole.WriteInfoLine("Attempting Linux build in podman");
            var folderName = Path.GetFileName(folder);

            var baseFolder = Path.GetFullPath(".");

            var podmanCommands = new List<string>
            {
                "set -e",
                "echo 'setting up build folder'",
                "mkdir /build",
                "rsync -ah /source/ /build/ --exclude builds --exclude bin --exclude obj --exclude .git",
                "echo 'copying succeeded'",
                "echo 'building...'",

                // Need to configure the build in the right way to do just the build we want in the container
                "cd /build && dotnet run --project Scripts -- package " +
                $"--podman false --compress false --installers false --dynamic-files false {platform}",
                "echo 'build finished'",
                "echo 'copying result'",
                $"rsync -vhr '/build/builds/{folderName}/' /out/ --delete",
                "echo 'result copied'",
            };

            var startInfo = new ProcessStartInfo("podman");
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--rm");

            // startInfo.ArgumentList.Add("--name=test");
            startInfo.ArgumentList.Add("-i");

            // Source mounted in read only way
            startInfo.ArgumentList.Add("--mount");
            startInfo.ArgumentList.Add($"type=bind,source={baseFolder},destination=/source,relabel=shared,ro=true");

            // Output folder mount allows writing
            startInfo.ArgumentList.Add("--mount");
            startInfo.ArgumentList.Add($"type=bind,source={folder},destination=/out,relabel=shared");

            startInfo.ArgumentList.Add(BuilderImageName);
            startInfo.ArgumentList.Add("/bin/bash");

            ColourConsole.WriteNormalLine("### Beginning podman build, following output is from the recursive build:");

            var result = await ProcessRunHelpers.RunProcessWithStdInAndOutputStreamingAsync(startInfo,
                cancellationToken, podmanCommands, ContainerOutput, ContainerOutput);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteWarningLine("Running podman failed. Has the build image been built?");
                return false;
            }

            ColourConsole.WriteNormalLine("###");
            ColourConsole.WriteNormalLine("### Podman run succeeded");
            ColourConsole.WriteNormalLine("###");
        }
        else
        {
            string runtime;

            switch (platform)
            {
                case PackagePlatform.Linux:
                    runtime = "linux-x64";
                    break;
                case PackagePlatform.Windows:
                    runtime = "win-x64";
                    break;
                case PackagePlatform.Windows32:
                    runtime = "win-x86";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
            }

            if (!await RunPublish(folder, runtime, cancellationToken))
            {
                return false;
            }
        }

        ColourConsole.WriteSuccessLine("Publish succeeded");
        return true;
    }

    protected override Task<bool> OnPostProcessExportedFolder(PackagePlatform platform, string folder,
        CancellationToken cancellationToken)
    {
        if (platform == PackagePlatform.Mac)
        {
            // Maybe some folder cleanup here?
        }

        PrunePdbFiles(folder);

        return Task.FromResult(true);
    }

    protected override async Task<bool> OnPostFolderHandled(PackagePlatform platform, string folderOrArchive,
        CancellationToken cancellationToken)
    {
        if (options.CreateInstallers == true)
        {
            ColourConsole.WriteInfoLine($"Creating installer for {platform} from {folderOrArchive}");
            throw new NotImplementedException();

            // AddReprintMessage();
        }

        return true;
    }

    protected override IEnumerable<FileToPackage> GetFilesToPackage()
    {
        if (options.CreateDynamicFiles == true)
        {
            yield return new FileToPackage(ReadmeFile, "README.txt");
            yield return new FileToPackage(RevisionFile, "revision.txt");
        }

        foreach (var fileToPackage in ExtraFilesToPackage)
        {
            yield return fileToPackage;
        }
    }

    private async Task CreateDynamicallyGeneratedFiles(CancellationToken cancellationToken)
    {
        await using var readme = File.CreateText(ReadmeFile);

        await readme.WriteLineAsync("Thrive Launcher");
        await readme.WriteLineAsync(string.Empty);
        await readme.WriteLineAsync(
            "This is a release of the Thrive Launcher. Run the executable 'ThriveLauncher' to open.");
        await readme.WriteLineAsync("The launcher allows downloading and playing available Thrive versions.");
        await readme.WriteLineAsync(string.Empty);
        await readme.WriteLineAsync(
            "Source code is available online: https://github.com/Revolutionary-Games/Thrive-Launcher");
        await readme.WriteLineAsync(string.Empty);
        await readme.WriteLineAsync("Exact commit this build is made from is in revision.txt");

        cancellationToken.ThrowIfCancellationRequested();

        await using var revision = File.CreateText(RevisionFile);

        await revision.WriteLineAsync(await GitRunHelpers.Log("./", 1, cancellationToken));
        await revision.WriteLineAsync(string.Empty);

        var diff = (await GitRunHelpers.Diff("./", cancellationToken, false, false)).Trim();

        if (!string.IsNullOrEmpty(diff))
        {
            await readme.WriteLineAsync("dirty, diff:");
            await readme.WriteLineAsync(diff);
        }
    }

    private async Task<bool> RunPublish(string folder, string runtime, CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine($"Publishing to folder: {folder}");

        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(runtime);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(folder);
        startInfo.ArgumentList.Add("ThriveLauncher");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteWarningLine("Publishing with dotnet failed");
            return false;
        }

        return true;
    }

    private void PrunePdbFiles(string folder)
    {
        ColourConsole.WriteNormalLine($"Pruning .pdb files in {folder}");

        foreach (var file in Directory.EnumerateFiles(folder, "*.pdb", SearchOption.AllDirectories))
        {
            ColourConsole.WriteDebugLine($"Removing pdb file: {file}");
            File.Delete(file);
        }
    }

    private void ContainerOutput(string line)
    {
        ColourConsole.WriteNormalLine($" {line}");
    }
}
