using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CommandLine;
using Scripts;
using ScriptsBase.Models;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class Program
{
    private static readonly IEnumerable<string> DefaultFoldersToClean = new List<string>
    {
        "LauncherBackend", "ThriveLauncher", "RevolutionaryGamesCommon/SharedBase",
    };

    [STAThread]
    public static int Main(string[] args)
    {
        RunFolderChecker.EnsureRightRunningFolder("ThriveLauncher.sln");

        var result = CommandLineHelpers.CreateParser()
            .ParseArguments<CheckOptions, TestOptions, ChangesOptions, IconsOptions, ContainerOptions,
                PackageOptions, CleanOptions>(args)
            .MapResult(
                (CheckOptions opts) => RunChecks(opts),
                (TestOptions opts) => RunTests(opts),
                (ChangesOptions opts) => RunChangesFinding(opts),
                (IconsOptions opts) => RunIcons(opts),
                (IconsOptions opts) => RunIcons(opts),
                (ContainerOptions options) => RunContainer(options),
                (PackageOptions options) => RunPackage(options),
                (CleanOptions opts) => RunClean(opts),
                CommandLineHelpers.PrintCommandLineErrors);

        ConsoleHelpers.CleanConsoleStateForExit();

        return result;
    }

    private static int RunChecks(CheckOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running in check mode");
        ColourConsole.WriteDebugLine($"Manually specified checks: {string.Join(' ', opts.Checks)}");

        var checker = new CodeChecks(opts);

        return checker.Run().Result;
    }

    private static int RunTests(TestOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running dotnet tests");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        return ProcessRunHelpers.RunProcessAsync(new ProcessStartInfo("dotnet", "test"), tokenSource.Token, false)
            .Result.ExitCode;
    }

    private static int RunChangesFinding(ChangesOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running changes finding tool");

        return OnlyChangedFileDetector.BuildListOfChangedFiles(opts).Result ? 0 : 1;
    }

    private static int RunIcons(IconsOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running icons tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        var checker = new IconProcessor(opts);

        return checker.Run(tokenSource.Token).Result ? 0 : 1;
    }

    private static int RunContainer(ContainerOptions options)
    {
        CommandLineHelpers.HandleDefaultOptions(options);

        ColourConsole.WriteDebugLine("Running container tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        var tool = new ContainerTool(options);

        return tool.Run(tokenSource.Token).Result ? 0 : 1;
    }

    private static int RunPackage(PackageOptions options)
    {
        CommandLineHelpers.HandleDefaultOptions(options);

        ColourConsole.WriteDebugLine("Running packaging tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        var packager = new PackageTool(options);

        return packager.Run(tokenSource.Token).Result ? 0 : 1;
    }

    private static int RunClean(CleanOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running cleaning tool");

        foreach (var folder in DefaultFoldersToClean)
        {
            if (!Directory.Exists(folder))
            {
                ColourConsole.WriteErrorLine($"Folder to clean in doesn't exist: {folder}");
                continue;
            }

            CleanIfExists(Path.Join(folder, "bin"));
            CleanIfExists(Path.Join(folder, "obj"));
        }

        return 0;
    }

    private static void CleanIfExists(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        ColourConsole.WriteNormalLine($"Deleting: {folder}");

        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception e)
        {
            ColourConsole.WriteErrorLine($"Failed to delete a folder ({folder}): {e}");
        }
    }

    public class CheckOptions : CheckOptionsBase
    {
    }

    [Verb("test", HelpText = "Run tests using 'dotnet' command")]
    public class TestOptions : ScriptOptionsBase
    {
    }

    public class ChangesOptions : ChangesOptionsBase
    {
        [Option('b', "branch", Required = false, Default = "master", HelpText = "The git remote branch name")]
        public override string RemoteBranch { get; set; } = "master";
    }

    [Verb("icons", HelpText = "Generate icons needed by the launcher from source files")]
    public class IconsOptions : ScriptOptionsBase
    {
    }

    public class ContainerOptions : ContainerOptionsBase
    {
        [Option('i', "image", Required = true, HelpText = "The image to build, either CI or ReleaseBuilder")]
        public ImageType? Image { get; set; }
    }

    public class PackageOptions : PackageOptionsBase
    {
        [Option('p', "podman", Required = false, Default = true,
            HelpText =
                "Use to set build to happen inside a container. Should be used when using a desktop OS to " +
                "make more widely compatible builds.")]
        public bool? LinuxPodman { get; set; }

        [Option('z', "compress", Default = true,
            HelpText = "Control whether the created folders are compressed into simple packages")]
        public bool? CompressRaw { get; set; }

        [Option('i', "installers", Default = true,
            HelpText = "When set installers are created after export")]
        public bool? CreateInstallers { get; set; }

        [Option("dynamic-files", Default = true,
            HelpText = "Can be used to skip generating dynamic files, for use in recursive builds")]
        public bool? CreateDynamicFiles { get; set; }

        [Option("no-runtime-variant", Default = false,
            HelpText = "Controls creating packages without runtime included")]
        public bool? CreateWithoutRuntime { get; set; }

        [Option("rcedit", Default = "rcedit-x64.exe",
            HelpText = "Name of the rcedit tool (required on non-Windows)")]
        public string RcEdit { get; set; } = "rcedit-x64.exe";

        public override bool Compress => CompressRaw == true;
    }

    [Verb("clean", HelpText = "Clean binaries (package upgrades can break deploy and this fixes that)")]
    public class CleanOptions : ScriptOptionsBase
    {
    }
}
