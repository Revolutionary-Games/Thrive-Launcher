namespace Scripts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class IconProcessor
{
    public const string BANNER_TARGET = "Scripts/installer_banner.bmp";

    private const string TARGET_PATH = "ThriveLauncher/Assets/Icons/";
    private const string TEMP_ICON_FOLDER = "Temp/Icons";
    private const string SOURCE_IMAGE = "ThriveLauncher/launcher-icon.png";

    private const string BANNER_SOURCE = "Scripts/installer_banner.png";

    private const string WINDOWS_MAGICK_EXECUTABLE = "magick.exe";

    public IconProcessor(Program.IconsOptions options)
    {
        _ = options;
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

        // Generate PNG sizes of the icons we'll need (some are only needed for flatpak packaging)
        foreach (var size in new[] { 16, 48, 128, 256, 512 })
        {
            if (!await CreateSingleImage(magick, size, ".png", cancellationToken, false))
                return false;
        }

        await CreateSingleImage(magick, 256, ".png", cancellationToken);
        await CreateSingleImage(magick, 128, ".png", cancellationToken);
        await CreateSingleImage(magick, 64, ".png", cancellationToken);
        await CreateSingleImage(magick, 32, ".png", cancellationToken);
        await CreateSingleImage(magick, 16, ".png", cancellationToken);

        if (OperatingSystem.IsMacOS())
        {
            if (!await CreateMacIcon(magick, cancellationToken))
                return false;
        }
        else
        {
            ColourConsole.WriteNormalLine("Mac icon creation is only possible on a mac");
        }

        // Windows
        if (!OperatingSystem.IsWindows())
            ColourConsole.WriteWarningLine("Creating .ico files on non-windows seems to result in lower quality");

        if (!await CreateMultiSize(magick, ".ico", cancellationToken))
            return false;

        // Installer banner, NSIS is very picky about the exact format here
        if (!await ConvertImage(magick, BANNER_SOURCE, $"BMP3:{BANNER_TARGET}", cancellationToken, "-compress", "none"))
        {
            return false;
        }

        ColourConsole.WriteSuccessLine("All icons generated successfully");
        return true;
    }

    private async Task<bool> CreateSingleImage(string magickExecutable, int size, string extension,
        CancellationToken cancellationToken, bool storeInTemporaryFolder = true)
    {
        var targetFolder = storeInTemporaryFolder ? TEMP_ICON_FOLDER : TARGET_PATH;

        var target = Path.Join(targetFolder, $"{size}x{size}{extension}");

        return await RunResize(magickExecutable, SOURCE_IMAGE, size, target, cancellationToken);
    }

    private async Task<bool> RunResize(string magickExecutable, string sourceFile, int size, string targetFile,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(magickExecutable);
        startInfo.ArgumentList.Add(sourceFile);
        startInfo.ArgumentList.Add("-resize");
        startInfo.ArgumentList.Add($"{size}x{size}");
        startInfo.ArgumentList.Add(targetFile);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to convert image");
            return false;
        }

        return true;
    }

    private async Task<bool> CreateMultiSize(string magickExecutable, string extension,
        CancellationToken cancellationToken)
    {
        // Approach from:
        // http://stackoverflow.com/questions/11423711/recipe-for-creating-windows-ico-files-with-imagemagick
        // This version keeps transparency. Ported from the earlier create_icons.rb script to C#

        var target = Path.Join(TARGET_PATH, $"icon{extension}");

        var startInfo = CreateStartInfo(magickExecutable);
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

    private async Task<bool> CreateMacIcon(string magickExecutable, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            throw new NotSupportedException("This relies on a mac specific tool");

        // ReSharper disable once StringLiteralTypo
        var iconSetName = "icon.iconset";

        var iconSetFolder = Path.Join(TEMP_ICON_FOLDER, iconSetName);

        if (Directory.Exists(iconSetFolder))
        {
            ColourConsole.WriteNormalLine("Deleting icon set generation temp folder before recreating it");
            Directory.Delete(iconSetFolder, true);
        }

        Directory.CreateDirectory(iconSetFolder);

        // Prepare all the png files needed for the mac icon (this is done here as these need very specific naming
        // and folder structure which is not needed elsewhere)
        foreach (var (size, doubled) in new[]
                 {
                     (16, false), (16, true), (32, false), (32, true), (128, false), (128, true), (256, false),
                     (256, true), (512, false),
                 })
        {
            string multiplier = string.Empty;

            int actualSize = size;

            if (doubled)
            {
                multiplier = "@2x";
                actualSize *= 2;
            }

            var file = Path.Join(iconSetFolder, $"icon_{size}x{size}{multiplier}.png");

            if (!await RunResize(magickExecutable, SOURCE_IMAGE, actualSize, file, cancellationToken))
            {
                return false;
            }
        }

        File.Copy(SOURCE_IMAGE, Path.Join(iconSetFolder, "icon_512x512@2x.png"));

        var temporaryTarget = Path.Join(TEMP_ICON_FOLDER, "icon.icns");

        var target = Path.Join(TARGET_PATH, "icon.icns");

        // We need to rely on a mac-specific tool here to do what we need
        var startInfo = new ProcessStartInfo("iconutil")
        {
            WorkingDirectory = TEMP_ICON_FOLDER,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("icns");
        startInfo.ArgumentList.Add(iconSetName);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to run icns file generation");
            return false;
        }

        if (File.Exists(target))
            File.Delete(target);

        File.Move(temporaryTarget, target);

        Directory.Delete(iconSetFolder, true);
        return true;
    }

    private async Task<bool> ConvertImage(string magickExecutable, string source, string target,
        CancellationToken cancellationToken, params string[] extraOptions)
    {
        var startInfo = CreateStartInfo(magickExecutable);
        startInfo.ArgumentList.Add(source);

        foreach (var extraOption in extraOptions)
        {
            startInfo.ArgumentList.Add(extraOption);
        }

        startInfo.ArgumentList.Add(target);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to convert image");
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
