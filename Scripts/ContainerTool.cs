namespace Scripts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ScriptsBase.ToolBases;
using ScriptsBase.Utilities;

public class ContainerTool : ContainerToolBase<Program.ContainerOptions>
{
    /// <summary>
    ///   Where the installer for the container is downloaded. This is done here to save us from having to install
    ///   curl in the container. This has to be updated when the base image is updated.
    /// </summary>
    private const string UbuntuDotnetInstallerDownload =
        "https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb";

    public ContainerTool(Program.ContainerOptions options) : base(options)
    {
        if (options.Image == null)
        {
            ColourConsole.WriteErrorLine("Image type must be selected");
            throw new ArgumentException("Image type not selected");
        }

        ColourConsole.WriteInfoLine($"Selected image type to build: {options.Image}");
    }

    protected override string ExportFileNameBase => options.Image switch
    {
        ImageType.CI => "launcher",
        ImageType.ReleaseBuilder => "launcher-builder",
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImagesAndConfigsFolder => Path.Join("Scripts", "podman");

    protected override (string BuildRelativeFolder, string? TargetToStopAt) DefaultImageToBuild => options.Image switch
    {
        ImageType.CI => ("ci", null),
        ImageType.ReleaseBuilder => ("release_builder", null),
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImageNameBase => $"thrive/{ExportFileNameBase}";

    private string PathToPackageFile =>
        Path.Join(ImagesAndConfigsFolder, "release_builder", "packages-microsoft-prod.deb");

    protected override IEnumerable<string> ImagesToPullIfTheyAreOld()
    {
        yield break;
    }

    protected override async Task<string?> Build(string buildType, string? targetToStopAt, string? tag,
        string? extraTag, CancellationToken cancellationToken)
    {
        if (options.Image == ImageType.ReleaseBuilder)
        {
            ColourConsole.WriteInfoLine(
                $"Downloading the dotnet install repo for use by the image to {PathToPackageFile}");

            var client = new HttpClient();

            try
            {
                var response = await client.GetAsync(UbuntuDotnetInstallerDownload,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var writer = File.OpenWrite(PathToPackageFile);
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                await stream.CopyToAsync(writer, cancellationToken);
            }
            catch (Exception e)
            {
                ColourConsole.WriteErrorLine($"Failed to download package repo file due to: {e}");
                return null;
            }

            ColourConsole.WriteNormalLine("Downloaded repo file");
        }

        return await base.Build(buildType, targetToStopAt, tag, extraTag, cancellationToken);
    }

    protected override Task<bool> PostCheckBuild(string tagOrId)
    {
        return CheckDotnetSdkWasInstalled(tagOrId);
    }
}
