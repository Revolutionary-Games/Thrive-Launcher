namespace Scripts;

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public static class SourceTool
{
    public static async Task<bool> Run(Program.SourceOptions opts, CancellationToken cancellationToken)
    {
        _ = opts;

        var launcherVersion = AssemblyInfoReader.ReadVersionFromCsproj(PackageTool.LauncherCsproj);

        var targetArchiveName = $"Thrive-Launcher-v{launcherVersion}.tar";
        var targetArchiveCompressed = targetArchiveName + ".xz";

        ColourConsole.WriteNormalLine("Exporting source archive with git");

        await GitRunHelpers.Archive(".", "HEAD", targetArchiveName, "Thrive-Launcher/", cancellationToken);

        var submodules = (await GitRunHelpers.SubmodulePaths(".", cancellationToken)).ToList();

        foreach (var submodule in submodules)
        {
            ColourConsole.WriteNormalLine($"Including source of submodule {submodule}");
            var moduleTar = submodule + ".tar";
            await GitRunHelpers.Archive(submodule, "HEAD", moduleTar, $"Thrive-Launcher/{submodule}/",
                cancellationToken);

            await Compression.CombineTar(targetArchiveName, moduleTar, cancellationToken);
        }

        ColourConsole.WriteNormalLine("Compressing source archive with xz");

        await Compression.XzCompressFile(targetArchiveName, cancellationToken, 9);

        if (!File.Exists(targetArchiveCompressed))
        {
            ColourConsole.WriteErrorLine("Compression failed to create the result file");
            return false;
        }

        ColourConsole.WriteSuccessLine($"Successfully created: {targetArchiveCompressed}");

        if (opts.CalculateSha256 != false)
        {
            var hash = await SHA256.HashDataAsync(File.OpenRead(targetArchiveCompressed), cancellationToken);

            ColourConsole.WriteNormalLine($"sha256: {Convert.ToHexString(hash).ToLowerInvariant()}");
        }

        return true;
    }
}
