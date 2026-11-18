namespace Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedBase.Models;
using Xunit;

public sealed class ThriveInstallerTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void ListFoldersInThriveInstallFolder_IncludesHiddenBetaFolders(bool showLatestBeta,
        bool showAllBetaVersions, int expectedVisibleBetaCount)
    {
        var installFolder = Path.Combine(Path.GetTempPath(), $"ThriveInstallerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(installFolder);

        try
        {
            const string olderBetaFolder = "Thrive_1.0.0-beta1_linux";
            const string latestBetaFolder = "Thrive_1.0.0-beta2_linux";
            const string stableFolder = "Thrive_1.0.0_linux";
            const string unrelatedFolder = "NotThrive";

            CreateFolder(installFolder, olderBetaFolder, "older beta contents");
            CreateFolder(installFolder, latestBetaFolder, "latest beta contents");
            CreateFolder(installFolder, stableFolder, "stable contents");
            CreateFolder(installFolder, unrelatedFolder, "unrelated contents");

            var platform = GetCurrentPlatform();
            var versions = new List<ThriveVersionLauncherInfo>
            {
                CreateVersion(1, "1.0.0", true, stableFolder, platform),
                CreateVersion(2, "1.0.0-beta1", false, olderBetaFolder, platform),
                CreateVersion(3, "1.0.0-beta2", false, latestBetaFolder, platform),
            };

            var info = new LauncherThriveInformation(new LauncherVersionInfo("1.0.0"), 1, versions,
                new Dictionary<string, DownloadMirrorInfo>())
            {
                LatestUnstable = 3,
            };

            var settings = new LauncherSettings
            {
                ThriveInstallationPath = installFolder,
                ShowLatestBetaVersion = showLatestBeta,
                ShowAllBetaVersions = showAllBetaVersions,
            };
            var installer = CreateInstaller(settings, info);

            var detectedFolders = installer.ListFoldersInThriveInstallFolder().ToDictionary(f => f.FolderName);

            Assert.True(detectedFolders[olderBetaFolder].IsThriveFolder);
            Assert.Equal("older beta contents".Length, detectedFolders[olderBetaFolder].Size);
            Assert.True(detectedFolders[latestBetaFolder].IsThriveFolder);
            Assert.True(detectedFolders[stableFolder].IsThriveFolder);
            Assert.False(detectedFolders[unrelatedFolder].IsThriveFolder);

            var playableBetaCount = installer.GetAvailableThriveVersions()
                .Count(v => v.VersionName.Contains("beta", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expectedVisibleBetaCount, playableBetaCount);
        }
        finally
        {
            Directory.Delete(installFolder, true);
        }
    }

    private static void CreateFolder(string installFolder, string folderName, string fileContents)
    {
        var folder = Directory.CreateDirectory(Path.Combine(installFolder, folderName));
        File.WriteAllText(Path.Combine(folder.FullName, "contents.txt"), fileContents);
    }

    private static PackagePlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsLinux())
            return PackagePlatform.Linux;

        if (OperatingSystem.IsWindows())
            return PackagePlatform.Windows;

        if (OperatingSystem.IsMacOS())
            return PackagePlatform.Mac;

        throw new PlatformNotSupportedException();
    }

    private static ThriveVersionLauncherInfo CreateVersion(int id, string releaseNumber, bool stable,
        string folderName, PackagePlatform platform)
    {
        var version = new ThriveVersionLauncherInfo(id, releaseNumber,
            new Dictionary<PackagePlatform, DownloadableInfo>
            {
                [platform] = new("1234567890", folderName,
                    new Dictionary<string, Uri> { ["mirror"] = new("https://example.com/download") }),
            })
        {
            Stable = stable,
        };

        return version;
    }

    private static ThriveInstaller CreateInstaller(LauncherSettings settings, LauncherThriveInformation info)
    {
        var settingsManager = Substitute.For<ILauncherSettingsManager>();
        settingsManager.Settings.Returns(settings);

        var infoRetriever = Substitute.For<IThriveAndLauncherInfoRetriever>();
        infoRetriever.CurrentlyLoadedInfo.Returns(info);

        var storeVersionDetector = Substitute.For<IStoreVersionDetector>();
        storeVersionDetector.Detect().Returns(new StoreVersionInfo());

        var launcherPaths = Substitute.For<ILauncherPaths>();
        var networkDataRetriever = Substitute.For<INetworkDataRetriever>();
        networkDataRetriever.UserAgent.Returns(new ProductInfoHeaderValue("Tests", "1.0"));

        var launcherTranslations = Substitute.For<ILauncherTranslations>();
        launcherTranslations.VersionWithPlatform.Returns("{0} {1}");
        launcherTranslations.LatestVersionTag.Returns("{0}");

        return new ThriveInstaller(NullLogger<ThriveInstaller>.Instance, settingsManager, infoRetriever,
            storeVersionDetector, launcherTranslations, Substitute.For<IDevCenterClient>(),
            launcherPaths, networkDataRetriever, Substitute.For<IExternalTools>(), Substitute.For<IRehydrator>(),
            new TemporaryFilesCleaner(settingsManager, launcherPaths, NullLogger<TemporaryFilesCleaner>.Instance));
    }
}
