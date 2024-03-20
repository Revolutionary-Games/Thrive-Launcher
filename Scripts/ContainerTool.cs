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
    private const string CentOsDotnetInstallerDownload =
        "https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm";

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
    protected override bool SaveByDefault => options.Image != ImageType.ReleaseBuilder;

    private string PathToPackageFile =>
        Path.Join(ImagesAndConfigsFolder, "release_builder", "packages-microsoft-prod.rpm");

    protected override IEnumerable<string> ImagesToPullIfTheyAreOld()
    {
        if (options.Image == ImageType.ReleaseBuilder)
            yield return "almalinux:9";
    }

    protected override async Task<string?> Build(string buildType, string? targetToStopAt, string? tag,
        string? extraTag, bool skipCache, CancellationToken cancellationToken)
    {
        if (options.Image == ImageType.ReleaseBuilder)
        {
            ColourConsole.WriteInfoLine(
                $"Downloading the dotnet install repo for use by the image to {PathToPackageFile}");

            var client = new HttpClient();

            try
            {
                var response = await client.GetAsync(CentOsDotnetInstallerDownload,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var writer = File.Open(PathToPackageFile, FileMode.Create);
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

        return await base.Build(buildType, targetToStopAt, tag, extraTag, skipCache, cancellationToken);
    }

    protected override Task<bool> PostCheckBuild(string tagOrId)
    {
        return CheckDotnetSdkWasInstalled(tagOrId);
    }
}
