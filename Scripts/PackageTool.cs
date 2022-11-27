namespace Scripts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Models;
using ScriptsBase.Models;
using ScriptsBase.ToolBases;
using ScriptsBase.Utilities;
using SharedBase.Models;
using SharedBase.Utilities;

public class PackageTool : PackageToolBase<Program.PackageOptions>
{
    public const string DotnetInstallerName = "windowsdesktop-runtime-6.0.11-win-x64.exe";
    public const string PathToDotnetInstaller = $"DependencyInstallers/{DotnetInstallerName}";

    private const string BuilderImageName = "localhost/thrive/launcher-builder:latest";
    private const string LauncherCsproj = "ThriveLauncher/ThriveLauncher.csproj";
    private const string LauncherExecutableIconFile = "ThriveLauncher/Assets/Icons/icon.ico";
    private const string LauncherInstallerBannerImageFile = "Scripts/installer_banner.bmp";
    private const string LauncherInstallerLicenseFile = "LICENSE.md";
    private const string NoRuntimeSuffix = "_without_runtime";

    private const string NSISFileName = "launcher.nsi";
    private const string NSISDotnetInstallerFileName = "launcher_dotnet_installer.nsi";
    private const string NSISTemplateFile = $"Scripts/{NSISFileName}.template";

    private const string LauncherAppName = "Thrive Launcher.app";
    private const string LauncherAppPlistTemplate = "Scripts/launcher.plist.template";
    private const string MacIcon = "ThriveLauncher/Assets/Icons/icon.icns";
    private const string MacInstallerBackground = "Scripts/mac_installer_background.png";
    private const string MacEntitlementsFile = "Scripts/ThriveLauncher.entitlements";
    private const string AssumedSelfSignedCertificateName = "SelfSigned";
    private const string MacDsStore = ".DS_Store";
    private const int DmgCreationRetries = 5;

    /// <summary>
    ///   Controls whether the mag .dmg and zip files just have the app or if they also have the readme files in
    ///   the root. License files are always accessible from the launcher licenses option once running, so these are
    ///   just a matter of taste if we want to include these.
    /// </summary>
    private const bool IncludeMacReadmeFilesInRoot = false;

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

    private static readonly IReadOnlyCollection<string> MacExecutablesToMerge = new List<string>
    {
        "ThriveLauncher",
    };

    private static readonly IReadOnlyCollection<string> MacFilesToJustCopy = new List<string>
    {
        "tools",

        // The native code libs already seem to be dual architecture so just copying is enough
        "libAvaloniaNative.dylib",
        "libHarfBuzzSharp.dylib",
        "libSkiaSharp.dylib",
    };

    private static readonly IReadOnlyCollection<string> MacFilesToSign = new List<string>
    {
        "ThriveLauncher",
        "libAvaloniaNative.dylib",
        "libHarfBuzzSharp.dylib",
        "libSkiaSharp.dylib",
    };

    private readonly string launcherVersion;

    /// <summary>
    ///   NSIS requires 4 numbers in the version always
    /// </summary>
    private readonly string launcherVersionAlwaysWithRevision;

    private bool doingNoRuntimeExport;
    private LauncherExportType currentExportType;
    private bool originalInstallerMode;

    public PackageTool(Program.PackageOptions options) : base(options)
    {
        // Retries don't really work for us so set it to 0
        options.Retries = 0;

        // For now it's starting to look like all builds need to be done on their native platforms to fully work
        var currentPlatform = PlatformUtilities.GetCurrentPlatform();
        DefaultPlatforms = new[] { LauncherPlatforms.First(p => p == currentPlatform) };

        launcherVersion = AssemblyInfoReader.ReadVersionFromCsproj(LauncherCsproj);

        var parsedVersion = new Version(launcherVersion);

        if (parsedVersion.Revision <= 0)
            parsedVersion = new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build, 0);

        launcherVersionAlwaysWithRevision = parsedVersion.ToString();
    }

    protected override IReadOnlyCollection<PackagePlatform> ValidPlatforms => LauncherPlatforms;

    protected override IEnumerable<PackagePlatform> DefaultPlatforms { get; }

    protected override IEnumerable<string> SourceFilesToPackage => SourceItemsToPackage;

    private string ReadmeFile => Path.Join(options.OutputFolder, "README.txt");
    private string RevisionFile => Path.Join(options.OutputFolder, "revision.txt");

    private string NSISInstallerName => doingNoRuntimeExport ?
        $"ThriveLauncher_Windows_Installer_WithDotnet_{launcherVersionAlwaysWithRevision}.exe" :
        $"ThriveLauncher_Windows_Installer_{launcherVersionAlwaysWithRevision}.exe";

    private string ExpectedLauncherInstallerFile => Path.Join(options.OutputFolder, NSISInstallerName);

    private string DMGInstallerName => $"ThriveLauncher_Mac_Installer_{launcherVersionAlwaysWithRevision}.dmg";
    private string ExpectedMacDMGFile => Path.Join(options.OutputFolder, DMGInstallerName);

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

        if (options.CreateWithoutRuntime == true)
        {
            ColourConsole.WriteWarningLine(
                "Creating exports without runtime included. For now these kinds of builds are not needed.");

            doingNoRuntimeExport = true;
        }

        if (options.ExportTypes.Count < 1)
        {
            ColourConsole.WriteInfoLine("Exporting by default with Standalone type");
            options.ExportTypes = new List<LauncherExportType>
            {
                LauncherExportType.Standalone,
            };
        }

        if (options.ExportTypes.Count < 1)
        {
            ColourConsole.WriteErrorLine("No export types selected");
            return false;
        }

        ColourConsole.WriteNormalLine($"Exporting with export types: {string.Join(" ", options.ExportTypes)}");

        originalInstallerMode = options.CreateInstallers == true;

        return true;
    }

    protected override string GetFolderNameForExport(PackagePlatform platform)
    {
        var name = ThriveProperties.GetFolderNameForLauncher(platform, launcherVersion, currentExportType);

        if (doingNoRuntimeExport)
            name = $"{name}{NoRuntimeSuffix}";

        return name;
    }

    protected override string GetCompressedExtensionForPlatform(PackagePlatform platform)
    {
        // Mac .app files in a zip are as good as the proper installer, also it's common for .app files to be
        // distributed as .zip files
        if (currentExportType == LauncherExportType.Standalone && platform != PackagePlatform.Mac)
        {
            return $"_standalone{base.GetCompressedExtensionForPlatform(platform)}";
        }

        return base.GetCompressedExtensionForPlatform(platform);
    }

    protected override CompressionType GetCompressionType(PackagePlatform platform)
    {
        if (platform == PackagePlatform.Mac)
            return CompressionType.Zip;

        return base.GetCompressionType(platform);
    }

    protected override bool CompressWithoutTopLevelFolder(PackagePlatform platform)
    {
        // Mac .app files should be directly compressed without extra level of folders
        return platform == PackagePlatform.Mac;
    }

    protected override async Task<bool> PackageForPlatform(CancellationToken cancellationToken,
        PackagePlatform platform)
    {
        if (doingNoRuntimeExport)
            ColourConsole.WriteInfoLine($"Doing a no runtime variant of export for {platform}");

        foreach (var exportType in options.ExportTypes)
        {
            options.CreateInstallers = originalInstallerMode;

            currentExportType = exportType;
            ColourConsole.WriteInfoLine($"Starting export with type {exportType}");

            switch (exportType)
            {
                case LauncherExportType.Standalone:
                case LauncherExportType.Steam:
                case LauncherExportType.Itch:
                    ColourConsole.WriteNormalLine("This export type doesn't have an installer");
                    options.CreateInstallers = false;
                    break;
            }

            if (!await base.PackageForPlatform(cancellationToken, platform))
                return false;
        }

        return true;
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

            // On mac we need to build both x64 and arm versions and then glue them together
            // This only works on a mac as this depends on Apple developer tools£

            var armFolder = $"{folder}-arm64";

            ColourConsole.WriteNormalLine("Exporting mac version for arm64...");

            Directory.CreateDirectory(armFolder);
            if (!await RunPublish(armFolder, "osx-arm64", !doingNoRuntimeExport, platform, cancellationToken))
            {
                return false;
            }

            var x64Folder = $"{folder}-x64";

            ColourConsole.WriteNormalLine("Exporting mac version for x64...");

            Directory.CreateDirectory(x64Folder);
            if (!await RunPublish(x64Folder, "osx-x64", !doingNoRuntimeExport, platform, cancellationToken))
            {
                return false;
            }

            // Merge the two
            ColourConsole.WriteNormalLine("Combining the mac files for universality");

            try
            {
                await RunMacUniversalMerge(folder, x64Folder, armFolder, cancellationToken);
            }
            catch (Exception)
            {
                ColourConsole.WriteErrorLine(
                    "Failed to create a merged universal mac binary, leaving separate binary folders undeleted");
                return false;
            }

            ColourConsole.WriteInfoLine(
                "Merged universal binaries should now be in the target folder. Removing arch specific folders.");
            Directory.Delete(armFolder, true);
            Directory.Delete(x64Folder, true);

            var appFolder = Path.Join(folder, LauncherAppName);

            if (Directory.Exists(appFolder))
            {
                ColourConsole.WriteNormalLine("Deleting existing .app folder");
                Directory.Delete(appFolder, true);
            }

            if (string.IsNullOrEmpty(options.MacSigningKey))
            {
                ColourConsole.WriteWarningLine(
                    "Signing without a specific key for mac (this should work but in an optimal case " +
                    "a signing key would be set)");
            }
            else
            {
                ColourConsole.WriteInfoLine($"Signing mac build with key {options.MacSigningKey}");
            }

            DeleteDsStore(folder);

            if (!await SignMacFilesRecursively(folder, cancellationToken))
            {
                return false;
            }
        }
        else if (platform == PackagePlatform.Linux && options.LinuxPodman == true)
        {
            if (options.NugetSource != null)
                ColourConsole.WriteWarningLine("Nuget source specifying doesn't work with podman build");

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
                $"--podman false --compress false --installers false --dynamic-files false {platform} " +
                $"--type {currentExportType}",
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

            if (!await RunPublish(folder, runtime, !doingNoRuntimeExport, platform, cancellationToken))
            {
                return false;
            }
        }

        ColourConsole.WriteSuccessLine("Publish succeeded");
        return true;
    }

    protected override async Task<bool> OnPostProcessExportedFolder(PackagePlatform platform, string folder,
        CancellationToken cancellationToken)
    {
        if (platform == PackagePlatform.Mac)
        {
            DeleteDsStore(folder);

            // We don't need to prune pdb files as the separate mac builds, made sure that no pdb files got copied
            // anyway
            ColourConsole.WriteInfoLine("Converting built mac folder to an .app");
            try
            {
                await CreateMacApp(folder, cancellationToken);
            }
            catch (Exception e)
            {
                ColourConsole.WriteErrorLine($"Failed to convert build files to an .app: {e}");
                return false;
            }

            // TODO: notarization
            ColourConsole.WriteWarningLine("TODO: notarization support");

            return true;
        }

        if (platform is PackagePlatform.Windows or PackagePlatform.Windows32)
        {
            if (!OperatingSystem.IsWindows())
            {
                ColourConsole.WriteInfoLine("Attempting to manually set right executable flags and metadata");

                await PostProcessWindowsFolder(folder, cancellationToken);
            }
            else
            {
                ColourConsole.WriteNormalLine("Assuming export on Windows already set right executable properties");
            }
        }

        PrunePdbFiles(folder);

        return true;
    }

    protected override async Task<bool> OnPostFolderHandled(PackagePlatform platform, string folderOrArchive,
        CancellationToken cancellationToken)
    {
        if (options.CreateInstallers != true)
            return true;

        if (currentExportType == LauncherExportType.Standalone)
            throw new InvalidOperationException("Should not attempt to create installers for standalone type");

        ColourConsole.WriteInfoLine($"Creating installer for {platform} from {folderOrArchive}");

        if (platform == PackagePlatform.Linux)
        {
            ColourConsole.WriteInfoLine("Linux installer is made with flatpak (hosted on Flathub)");
            AddReprintMessage("Linux installer needs to be separately updated for Flathub");

            // TODO: copy the flatpakref file to the build folder
        }
        else if (platform is PackagePlatform.Windows or PackagePlatform.Windows32)
        {
            if (platform == PackagePlatform.Windows32)
            {
                throw new NotImplementedException(
                    "Windows32 installer needs a suffix or something to not conflict");
            }

            var nsisSource = ConvertArchiveNameToFolderName(platform, folderOrArchive);

            var nsisFileName = NSISFileName;
            var nsisTemplate = NSISTemplateFile;

            if (doingNoRuntimeExport)
            {
                ColourConsole.WriteNormalLine(
                    "Windows installer without runtime will attempt to install the runtime, " +
                    $"please make sure the runtime installer exists at {PathToDotnetInstaller}");

                nsisFileName = NSISDotnetInstallerFileName;
            }

            // Windows installer is made with NSIS
            await GenerateNSISFile(nsisSource, nsisFileName, nsisTemplate, cancellationToken);
            await RunNSIS(nsisFileName, cancellationToken);

            if (!File.Exists(ExpectedLauncherInstallerFile))
            {
                ColourConsole.WriteErrorLine("Expected installer file did not get created");
                return false;
            }

            var hash = FileUtilities.HashToHex(
                await FileUtilities.CalculateSha3OfFile(ExpectedLauncherInstallerFile, cancellationToken));

            var message1 = $"Created {platform} installer: {ExpectedLauncherInstallerFile}";
            var message2 = $"SHA3: {hash}";

            AddReprintMessage(string.Empty);
            AddReprintMessage(message1);
            AddReprintMessage(message2);

            ColourConsole.WriteSuccessLine(message1);
            ColourConsole.WriteNormalLine(message2);
        }
        else if (platform == PackagePlatform.Mac)
        {
            if (!await CreateMacDMG(ConvertArchiveNameToFolderName(platform, folderOrArchive), ExpectedMacDMGFile,
                    cancellationToken))
            {
                return false;
            }

            if (!File.Exists(ExpectedMacDMGFile))
            {
                ColourConsole.WriteErrorLine("Expected mac installer file did not get created");
                return false;
            }

            // TODO: notarization (also needed for .dmg even when the app inside is notarized already)
            ColourConsole.WriteWarningLine("TODO: notarization support");

            var hash = FileUtilities.HashToHex(
                await FileUtilities.CalculateSha3OfFile(ExpectedMacDMGFile, cancellationToken));

            var message1 = $"Created {platform} installer: {ExpectedMacDMGFile}";
            var message2 = $"SHA3: {hash}";

            AddReprintMessage(string.Empty);
            AddReprintMessage(message1);
            AddReprintMessage(message2);

            ColourConsole.WriteSuccessLine(message1);
            ColourConsole.WriteNormalLine(message2);
        }
        else
        {
            throw new NotSupportedException("unsupported target platform for installer creation");
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

    private async Task<bool> RunPublish(string folder, string runtime, bool selfContained, PackagePlatform platform,
        CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine($"Publishing to folder: {folder}");

        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(runtime);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add(selfContained ? "true" : "false");

        if (options.NugetSource != null)
        {
            ColourConsole.WriteNormalLine($"Using nuget source: {options.NugetSource}");
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(options.NugetSource);
        }

        // Compiler definitions
        switch (currentExportType)
        {
            default:
            case LauncherExportType.Standalone:

                // Mac .app files can be considered to support updating just fine
                if (platform == PackagePlatform.Mac)
                {
                    startInfo.ArgumentList.Add("-p:MyConstants=\"LAUNCHER_UPDATER_MAC\"");
                }

                break;
            case LauncherExportType.WithUpdater:
                switch (platform)
                {
                    // TODO: should this copy the flatpak ref file here?
                    // case PackagePlatform.Linux:
                    //     break;
                    case PackagePlatform.Windows:
                        startInfo.ArgumentList.Add(
                            "-p:MyConstants=\"LAUNCHER_UPDATER_WINDOWS\"");
                        break;
                    case PackagePlatform.Windows32:
                        throw new NotImplementedException("Windows 32-bit auto update is not done");
                    case PackagePlatform.Mac:
                        startInfo.ArgumentList.Add("-p:MyConstants=\"LAUNCHER_UPDATER_MAC\"");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(platform), platform,
                            "Auto update type unknown for platform");
                }

                break;
            case LauncherExportType.Steam:
                startInfo.ArgumentList.Add("-p:MyConstants=\"LAUNCHER_STEAM;LAUNCHER_NO_OUTDATED_NOTICE\"");
                break;
            case LauncherExportType.Itch:
                startInfo.ArgumentList.Add(
                    "-p:MyConstants=\"LAUNCHER_ITCH;LAUNCHER_DELAYED_UPDATE_NOTICE\"");
                break;
            case LauncherExportType.Flatpak:
                startInfo.ArgumentList.Add(
                    "-p:MyConstants=\"LAUNCHER_DELAYED_UPDATE_NOTICE\"");
                break;
        }

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

    private async Task PostProcessWindowsFolder(string folder, CancellationToken cancellationToken)
    {
        var executable = Path.Join(folder, "ThriveLauncher.exe");

        // The following breaks some signature check or something so we cannot use it
        // Even using a different tool breaks the executable when the version or icon is touched
        var message = "Cannot set executable icon or version on Linux. This results in pretty bad Windows builds.";
        ColourConsole.WriteErrorLine(message);
        AddReprintMessage(message);

        // var versionData = AssemblyInfoReader.ReadAllProjectVersionMetadata(LauncherCsproj);

        // await RunRcEdit(executable, cancellationToken, "--set-icon", LauncherExecutableIconFile,
        //     "--set-version-string", "ProductName", "Thrive Launcher",
        //     "--set-version-string", "CompanyName", versionData.Authors,
        //     "--set-version-string", "FileDescription", versionData.Description,
        //     "--set-version-string", "LegalCopyright", versionData.Copyright
        //     "--set-version-string", "FileVersion", versionData.Version,
        //     "--set-version-string", "ProductVersion", versionData.Version);

        // This seems to require setting separately to stick
        // await RunRcEdit(executable, cancellationToken, "--set-product-version", versionData.Version);

        // This seems to luckily not break *everything* but this isn't really good enough
        using var modifier = new PEModifier(executable);

        await modifier.SetExecutableToGUIMode(cancellationToken);

        ColourConsole.WriteNormalLine($"Executable ({executable}) modified");
    }

    private async Task RunMacUniversalMerge(string targetFolder, string firstDirectory, string secondDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var mergedName in MacExecutablesToMerge)
        {
            ColourConsole.WriteDebugLine($"Merging mac executable: {mergedName}");

            var target = Path.Join(targetFolder, mergedName);

            if (File.Exists(target))
                File.Delete(target);

            if (!await RunLipo(target, Path.Join(firstDirectory, mergedName), Path.Join(secondDirectory, mergedName),
                    cancellationToken))
            {
                ColourConsole.WriteErrorLine($"Failed to merge {mergedName}");
                throw new Exception("Running merge failed");
            }
        }

        foreach (var fileName in MacFilesToJustCopy)
        {
            ColourConsole.WriteDebugLine($"Just moving mac resource from first folder: {fileName}");

            var source = Path.Join(firstDirectory, fileName);

            var target = Path.Join(targetFolder, fileName);

            if (Directory.Exists(target))
                Directory.Delete(target, true);

            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target, true);
            }
        }
    }

    private async Task<bool> RunLipo(string target, string source1, string source2, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("xcrun");
        startInfo.ArgumentList.Add("lipo");
        startInfo.ArgumentList.Add("-create");
        startInfo.ArgumentList.Add("-output");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add(source1);
        startInfo.ArgumentList.Add(source2);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Running lipo failed. Are xcode tools installed?");
            return false;
        }

        return true;
    }

    private async Task<bool> SignMacFilesRecursively(string folder, CancellationToken cancellationToken)
    {
        // Signing the final .app requires us to sign *everything* in the MacOS folder, so that's what we need to do
        // here
        foreach (var executable in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!await SignFileForMac(executable, cancellationToken))
                return false;
        }

        return true;
    }

    private async Task<bool> SignFileForMac(string filePath, CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine($"Signing {filePath}");

        var startInfo = new ProcessStartInfo("xcrun");
        startInfo.ArgumentList.Add("codesign");
        startInfo.ArgumentList.Add("--force");
        startInfo.ArgumentList.Add("--verbose");
        startInfo.ArgumentList.Add("--timestamp");

        startInfo.ArgumentList.Add("--sign");

        AddCodesignName(startInfo);

        startInfo.ArgumentList.Add("--options=runtime");
        startInfo.ArgumentList.Add("--entitlements");
        startInfo.ArgumentList.Add(MacEntitlementsFile);
        startInfo.ArgumentList.Add(filePath);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine(
                $"Running codesign on '{filePath}' failed. " +
                "Are xcode tools installed and do you have the right certificates installed / " +
                "self-signed certificate created?");
            return false;
        }

        ColourConsole.WriteDebugLine("Codesign succeeded");

        return true;
    }

    private void AddCodesignName(ProcessStartInfo startInfo)
    {
        if (!string.IsNullOrEmpty(options.MacSigningKey))
        {
            startInfo.ArgumentList.Add(options.MacSigningKey);
        }
        else
        {
            startInfo.ArgumentList.Add(AssumedSelfSignedCertificateName);
        }
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

    private string ConvertArchiveNameToFolderName(PackagePlatform platform, string folderOrArchive)
    {
        string result = folderOrArchive;

        var potentialExtension = GetCompressedExtensionForPlatform(platform);
        if (folderOrArchive.EndsWith(potentialExtension))
            result = result.Substring(0, result.Length - potentialExtension.Length);
        return result;
    }

    private void ContainerOutput(string line)
    {
        ColourConsole.WriteNormalLine($" {line}");
    }

    private async Task RunRcEdit(string executable, CancellationToken cancellationToken, params string[] arguments)
    {
        ColourConsole.WriteNormalLine($"Running {options.RcEdit} on: {executable}");

        string pathToRcEdit;

        if (!File.Exists(options.RcEdit))
        {
            pathToRcEdit = ExecutableFinder.Which(options.RcEdit) ??
                throw new Exception("Could not find rcedit in PATH");
        }
        else
        {
            pathToRcEdit = options.RcEdit;
        }

        ProcessStartInfo startInfo;
        if (!OperatingSystem.IsWindows())
        {
            // It seems to work even without wine, but for clarify of what's happening this will try to run through
            // wine explicitly
            startInfo = new ProcessStartInfo(ExecutableFinder.Which("wine") ??
                throw new Exception("Wine is not installed"));
            startInfo.ArgumentList.Add(pathToRcEdit);
        }
        else
        {
            startInfo = new ProcessStartInfo(pathToRcEdit);
        }

        startInfo.ArgumentList.Add(executable);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteWarningLine("Running rcedit failed. Is it installed?");
            throw new Exception($"rcedit exited with {result.ExitCode}");
        }
    }

    private async Task GenerateNSISFile(string sourceFolder, string nsisFileName, string templateFile,
        CancellationToken cancellationToken)
    {
        var target =
            Path.Combine(Path.GetDirectoryName(sourceFolder) ?? throw new ArgumentException("Can't get parent folder"),
                nsisFileName);

        var versionData = AssemblyInfoReader.ReadAllProjectVersionMetadata(LauncherCsproj);

        ColourConsole.WriteNormalLine($"Generating NSIS config at {target} with source folder: {sourceFolder}");

        var templateText = await File.ReadAllTextAsync(templateFile, Encoding.UTF8, cancellationToken);

        var dotnetMode = "# ";
        var installerName = NSISInstallerName;

        if (doingNoRuntimeExport)
        {
            dotnetMode = string.Empty;
        }

        var replacedVariables = new Dictionary<string, string>
        {
            { "REPLACE_TEMPLATE_VERSION", launcherVersionAlwaysWithRevision },
            { "REPLACE_TEMPLATE_ICON_FILE", PrepareNSISPath(LauncherExecutableIconFile) },
            { "REPLACE_TEMPLATE_BANNER_IMAGE_FILE", PrepareNSISPath(LauncherInstallerBannerImageFile) },
            { "REPLACE_TEMPLATE_PATH_TO_LICENSE", PrepareNSISPath(LauncherInstallerLicenseFile) },
            { "REPLACE_TEMPLATE_SOURCE_DIRECTORY", PrepareNSISPath(sourceFolder) },
            { "REPLACE_TEMPLATE_DESCRIPTION", versionData.Description },
            { "REPLACE_TEMPLATE_COPYRIGHT", versionData.Copyright },
            { "REPLACE_TEMPLATE_DOTNET_INSTALLER_NAME", DotnetInstallerName },
            { "REPLACE_TEMPLATE_PATH_TO_DOTNET_INSTALLER", PrepareNSISPath(PathToDotnetInstaller) },
            { "TEMPLATE_MODE_DOTNET;", dotnetMode },
            { "REPLACE_TEMPLATE_INSTALLER_NAME", installerName },
        };

        string finalText = templateText;

        foreach (var (variable, replacingText) in replacedVariables)
        {
            finalText = finalText.Replace(variable, replacingText);
        }

        await File.WriteAllTextAsync(target, finalText, new UTF8Encoding(true), cancellationToken);
    }

    private string PrepareNSISPath(string rawPath)
    {
        var full = Path.GetFullPath(rawPath);

        // Some places only allow double \\ for paths, so to be safe we use that everywhere
        return full.Replace("/", @"\").Replace(@"\", @"\\").Replace(@"\\\", @"\\");
    }

    private async Task RunNSIS(string nsisFileName, CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine($"Running makensis on {Path.Join(options.OutputFolder, nsisFileName)}");

        var startInfo = new ProcessStartInfo("makensis")
        {
            WorkingDirectory = options.OutputFolder,
        };

        startInfo.ArgumentList.Add(nsisFileName);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteWarningLine("Running makensis failed. Is it installed?");
            throw new Exception($"makensis exited with {result.ExitCode}");
        }

        ColourConsole.WriteSuccessLine("Running makensis succeeded");
    }

    private async Task CreateMacApp(string folder, CancellationToken cancellationToken)
    {
        var appBase = Path.Join(folder, LauncherAppName);

        if (Directory.Exists(appBase))
            Directory.Delete(appBase, true);

        Directory.CreateDirectory(appBase);

        ColourConsole.WriteNormalLine($"Generating app {appBase}");

        var contents = Path.Join(appBase, "Contents");
        var macFolder = Path.Join(contents, "MacOS");
        var resourcesFolder = Path.Join(contents, "Resources");

        Directory.CreateDirectory(contents);
        Directory.CreateDirectory(macFolder);
        Directory.CreateDirectory(resourcesFolder);

        CopyHelpers.CopyToFolder(MacIcon, resourcesFolder);

        // Move the created stuff in the build folder to the MacOS folder as that's where the executable needs to be
        // so we might as move everything (except a few things that are useful for .zip creation) to there
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
        {
            // Ignore some files we may not move
#pragma warning disable CS0162
            if (entry.EndsWith(LauncherAppName) || entry.Contains(MacDsStore) ||
                (!IncludeMacReadmeFilesInRoot && (entry.EndsWith(".txt") || entry.EndsWith(".md"))))
            {
                continue;
            }
#pragma warning restore CS0162

            ColourConsole.WriteDebugLine($"Moving {entry} -> {macFolder}");

            if (Directory.Exists(entry))
            {
                Directory.Move(entry, Path.Join(macFolder, Path.GetFileName(entry)));
            }
            else
            {
                CopyHelpers.MoveToFolder(entry, macFolder);
            }
        }

        // Move some stuff to be more where Apple says they should be
        MoveReadmeFiles(Path.Join(macFolder, "tools", "pck"), Path.Join(resourcesFolder, "godotpcktool"));
        MoveReadmeFiles(Path.Join(macFolder, "tools", "7zip"), Path.Join(resourcesFolder, "7zip"));

        MoveReadmeFiles(macFolder, Path.Join(resourcesFolder, "ReadmeFiles"));

        // TODO: remove workaround once Avalonia works right on mac
        // ReSharper disable StringLiteralTypo
        File.Move(Path.Join(macFolder, "libAvaloniaNative.dylib"), Path.Join(macFolder, "liblibAvaloniaNative.dylib"));

        // ReSharper restore StringLiteralTypo

        // Setup the plist
        ColourConsole.WriteNormalLine("Setting up plist for app");

        var templateText = await File.ReadAllTextAsync(LauncherAppPlistTemplate, Encoding.UTF8, cancellationToken);

        var versionData = AssemblyInfoReader.ReadAllProjectVersionMetadata(LauncherCsproj);

        var replacedVariables = new Dictionary<string, string>
        {
            { "REPLACE_TEMPLATE_USER_VERSION", launcherVersion },
            { "REPLACE_TEMPLATE_VERSION", launcherVersionAlwaysWithRevision },
            { "REPLACE_TEMPLATE_COPYRIGHT", versionData.Copyright },
        };

        string finalText = templateText;

        foreach (var (variable, replacingText) in replacedVariables)
        {
            finalText = finalText.Replace(variable, replacingText);
        }

        await File.WriteAllTextAsync(Path.Join(contents, "Info.plist"), finalText, new UTF8Encoding(false),
            cancellationToken);

        // The base app file must be signed as well as all of the executables
        if (!await SignFileForMac(appBase, cancellationToken))
            throw new Exception("Signing .app file failed");

        ColourConsole.WriteSuccessLine($"App created at {appBase}");
    }

    private void MoveReadmeFiles(string fromFolder, string toFolder)
    {
        Directory.CreateDirectory(toFolder);

        foreach (var file in Directory.EnumerateFiles(fromFolder, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".txt") || file.EndsWith(".md") || file.EndsWith("LICENSE"))
            {
                CopyHelpers.MoveToFolder(file, toFolder);
            }
        }
    }

    private async Task<bool> CreateMacDMG(string folder, string dmgToCreate, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            ColourConsole.WriteErrorLine("Folder to make into dmg doesn't exist");
            return false;
        }

        ColourConsole.WriteNormalLine($"Creating .dmg installer from folder {folder}");
        ColourConsole.WriteWarningLine(
            "This is a bit buggy so please check the result / run this multiple times if the visual " +
            "customization fails");

        if (File.Exists(dmgToCreate))
        {
            ColourConsole.WriteNormalLine("Deleting existing .dmg before recreating it");
            File.Delete(dmgToCreate);
        }

        // Retry running this a few times as this has a chance to fail spuriously
        for (int i = 0; i < DmgCreationRetries; ++i)
        {
            if (i > 0)
            {
                ColourConsole.WriteWarningLine("Waiting until retrying .dmg creation...");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }

            var startInfo = new ProcessStartInfo("create-dmg/create-dmg");

            // ReSharper disable StringLiteralTypo
            startInfo.ArgumentList.Add("--volname");
            startInfo.ArgumentList.Add("Thrive Launcher");
            startInfo.ArgumentList.Add("--volicon");
            startInfo.ArgumentList.Add(MacIcon);
            startInfo.ArgumentList.Add("--background");
            startInfo.ArgumentList.Add(MacInstallerBackground);
            startInfo.ArgumentList.Add("--icon");
            startInfo.ArgumentList.Add("Thrive Launcher.app");
            startInfo.ArgumentList.Add("140");

            // Do we want to align the icon text or visually where their tops are?
            startInfo.ArgumentList.Add("120");

            // Visual alignment
            // startInfo.ArgumentList.Add("130");

            startInfo.ArgumentList.Add("--hide-extension");
            startInfo.ArgumentList.Add("Thrive Launcher.app");

            startInfo.ArgumentList.Add("--app-drop-link");
            startInfo.ArgumentList.Add("440");
            startInfo.ArgumentList.Add("120");

            startInfo.ArgumentList.Add("--icon-size");
            startInfo.ArgumentList.Add("80");

            startInfo.ArgumentList.Add("--window-pos");
            startInfo.ArgumentList.Add("200");
            startInfo.ArgumentList.Add("120");

            startInfo.ArgumentList.Add("--window-size");
            startInfo.ArgumentList.Add("650");
            startInfo.ArgumentList.Add("350");

            startInfo.ArgumentList.Add("--no-internet-enable");

            startInfo.ArgumentList.Add("--codesign");
            AddCodesignName(startInfo);

            // TODO: notarization
            // startInfo.ArgumentList.Add("--notarize");
            // startInfo.ArgumentList.Add(notarizationCredentials);

            startInfo.ArgumentList.Add(dmgToCreate);
            startInfo.ArgumentList.Add(folder);

            // ReSharper restore StringLiteralTypo

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteWarningLine($"Running dmg creation failed. Exit code: {result.ExitCode}");
            }
            else
            {
                ColourConsole.WriteSuccessLine($"Created {dmgToCreate}");
                return true;
            }
        }

        ColourConsole.WriteErrorLine($"dmg creator failed too many times");
        return false;
    }

    private void DeleteDsStore(string folder)
    {
        var file = Path.Join(folder, MacDsStore);

        if (File.Exists(file))
        {
            ColourConsole.WriteDebugLine($"Deleting mac folder settings: {file}");
            File.Delete(file);
        }
    }
}
