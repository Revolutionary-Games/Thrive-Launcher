namespace Scripts;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class IconProcessor
{
    private const string TARGET_PATH = "ThriveLauncher/Assets/Icons/";
    private const string TEMP_ICON_FOLDER = "Temp/Icons";
    private const string SOURCE_IMAGE = "ThriveLauncher/launcher-icon.png";

    private const string WINDOWS_MAGICK_EXECUTABLE = "magick.exe";

    private readonly Program.IconsOptions options;

    public IconProcessor(Program.IconsOptions options)
    {
        this.options = options;
    }

    public async Task<bool> Run(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(TARGET_PATH);
        Directory.CreateDirectory(TEMP_ICON_FOLDER);

        if (!File.Exists(SOURCE_IMAGE))
        {
            ColourConsole.WriteErrorLine($"Missing icon source: {SOURCE_IMAGE}");
            return false;
        }

        var magick = FindMagickExecutable();

        if (magick == null)
        {
            ColourConsole.WriteErrorLine(
                "Could not find ImageMagick. Please make sure it is installed and in PATH and then reopen " +
                "terminal and try again");
            return false;
        }

        await CreateSingleImage(magick, 256, ".png", cancellationToken, false);

        await CreateSingleImage(magick, 256, ".png", cancellationToken);
        await CreateSingleImage(magick, 128, ".png", cancellationToken);
        await CreateSingleImage(magick, 64, ".png", cancellationToken);
        await CreateSingleImage(magick, 32, ".png", cancellationToken);
        await CreateSingleImage(magick, 16, ".png", cancellationToken);

        // This depends on the temp folder png icons
        await CreateMacIcon(magick, cancellationToken);

        // Windows
        await CreateMultiSize(magick, ".ico", cancellationToken);

        ColourConsole.WriteSuccessLine("All icons generated successfully");
        return true;
    }

    private async Task<bool> CreateSingleImage(string magicExecutable, int size, string extension,
        CancellationToken cancellationToken, bool storeInTemporaryFolder = true)
    {
        var targetFolder = storeInTemporaryFolder ? TEMP_ICON_FOLDER : TARGET_PATH;

        var target = Path.Join(targetFolder, $"{size}x{size}{extension}");

        var startInfo = CreateStartInfo(magicExecutable);
        startInfo.ArgumentList.Add(SOURCE_IMAGE);
        startInfo.ArgumentList.Add("-resize");
        startInfo.ArgumentList.Add($"{size}x{size}");
        startInfo.ArgumentList.Add(target);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to convert image");
            return false;
        }

        return true;
    }

    private async Task<bool> CreateMultiSize(string magicExecutable, string extension,
        CancellationToken cancellationToken)
    {
        // Approach from:
        // http://stackoverflow.com/questions/11423711/recipe-for-creating-windows-ico-files-with-imagemagick
        // This version keeps transparency. Ported from the earlier create_icons.rb script to C#

        var target = Path.Join(TARGET_PATH, $"icon{extension}");

        var startInfo = CreateStartInfo(magicExecutable);
        startInfo.ArgumentList.Add(SOURCE_IMAGE);

        foreach (var size in new[] { 16, 32, 48, 64, 128, 256 })
        {
            startInfo.ArgumentList.Add("(");
            startInfo.ArgumentList.Add("-clone");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-resize");
            startInfo.ArgumentList.Add($"{size}x{size}");
            startInfo.ArgumentList.Add(")");
        }

        startInfo.ArgumentList.Add("-delete");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-colors");
        startInfo.ArgumentList.Add("256");
        startInfo.ArgumentList.Add(target);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to convert image (with multiple sizes)");
            return false;
        }

        return true;
    }

    private async Task<bool> CreateMacIcon(string magicExecutable, CancellationToken cancellationToken)
    {
        // Apparently this doesn't work for some reason (or it does but doesn't display right on other platforms)

        // looks like the only solution is to use this: https://github.com/pornel/libicns

        var target = Path.Join(TARGET_PATH, "icon.icns");

        var startInfo = CreateStartInfo(magicExecutable);

        foreach (var size in new[] { 16, 32, 64, 128, 256 }.Reverse())
        {
            startInfo.ArgumentList.Add(Path.Join(TEMP_ICON_FOLDER, $"{size}x{size}.png"));
        }

        startInfo.ArgumentList.Add(target);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to convert image (with multiple sizes)");
            return false;
        }

        return true;
    }

    private string? FindMagickExecutable()
    {
        foreach (var candidate in GetPotentialMagickNames())
        {
            var found = ExecutableFinder.Which(candidate);

            if (found != null)
            {
                ColourConsole.WriteDebugLine($"Detected magick at: {found}");
                return found;
            }
        }

        return null;
    }

    private IEnumerable<string> GetPotentialMagickNames()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows uses a common magick.exe as the tool name instead of separate executables
            // for all the tools like on Linux
            return new[] { WINDOWS_MAGICK_EXECUTABLE };
        }

        return new[] { "convert" };
    }

    private ProcessStartInfo CreateStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
        };

        if (executable == WINDOWS_MAGICK_EXECUTABLE)
        {
            // See the comment in GetPotentialMagickNames
            startInfo.ArgumentList.Add("convert");
        }

        return startInfo;
    }
}
