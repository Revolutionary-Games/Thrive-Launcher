namespace Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public sealed class ThriveRunnerTests
{
    [Fact]
    public void StartThrive_ClearsExistingCrashDumpsFromDefaultFolder()
    {
        var testFolder = CreateTestFolder();
        var crashesFolder = Path.Combine(testFolder, "crashes");
        var nestedFolder = Path.Combine(crashesFolder, "nested");
        Directory.CreateDirectory(nestedFolder);

        try
        {
            var rootDump = Path.Combine(crashesFolder, "root.dmp");
            var nestedDump = Path.Combine(nestedFolder, "nested.dmp");
            var unrelatedFile = Path.Combine(nestedFolder, "keep.txt");
            File.WriteAllText(rootDump, "old root dump");
            File.WriteAllText(nestedDump, "old nested dump");
            File.WriteAllText(unrelatedFile, "unrelated file");

            var runner = CreateRunner(testFolder, crashesFolder);

            runner.StartThrive(Substitute.For<IPlayableVersion>(), false, CancellationToken.None);

            Assert.False(File.Exists(rootDump));
            Assert.False(File.Exists(nestedDump));
            Assert.True(File.Exists(unrelatedFile));

            var newDump = Path.Combine(crashesFolder, "new.dmp");
            File.WriteAllText(newDump, "new dump");
            Assert.Contains(runner.GetAvailableCrashesToReport().OfType<ReportableCrashDump>(),
                crash => crash.File == newDump);
        }
        finally
        {
            Directory.Delete(testFolder, true);
        }
    }

    [Fact]
    public void StartThrive_ClearsDumpsFromDetectedLocations()
    {
        var testFolder = CreateTestFolder();
        var dataFolder = Path.Combine(testFolder, "data");
        var crashesFolder = Path.Combine(dataFolder, "crashes");
        var nestedFolder = Path.Combine(crashesFolder, "nested");
        Directory.CreateDirectory(nestedFolder);

        try
        {
            var latestDump = Path.Combine(testFolder, "reported.dmp");
            var dataDump = Path.Combine(crashesFolder, "data.dmp");
            var nestedDump = Path.Combine(nestedFolder, "nested.dmp");
            File.WriteAllText(latestDump, "latest dump");
            File.WriteAllText(dataDump, "data dump");
            File.WriteAllText(nestedDump, "nested dump");

            var runner = CreateRunner(testFolder, Path.Combine(testFolder, "default-crashes"));
            SetPrivateProperty(runner, nameof(ThriveRunner.DetectedThriveDataFolder), dataFolder);
            SetPrivateProperty(runner, nameof(ThriveRunner.DetectedCrashDumpOutputLocation), latestDump);

            runner.StartThrive(Substitute.For<IPlayableVersion>(), false, CancellationToken.None);

            Assert.False(File.Exists(latestDump));
            Assert.False(File.Exists(dataDump));
            Assert.False(File.Exists(nestedDump));
        }
        finally
        {
            Directory.Delete(testFolder, true);
        }
    }

    [Fact]
    public void StartThrive_ToleratesMissingCrashFolder()
    {
        var testFolder = CreateTestFolder();

        try
        {
            var runner = CreateRunner(testFolder, Path.Combine(testFolder, "missing-crashes"));

            runner.StartThrive(Substitute.For<IPlayableVersion>(), false, CancellationToken.None);

            Assert.False(runner.ThriveRunning);
        }
        finally
        {
            Directory.Delete(testFolder, true);
        }
    }

    [Fact]
    public void ClearOutput_DoesNotDeleteCrashDumps()
    {
        var testFolder = CreateTestFolder();
        var crashesFolder = Path.Combine(testFolder, "crashes");
        Directory.CreateDirectory(crashesFolder);

        try
        {
            var dump = Path.Combine(crashesFolder, "existing.dmp");
            File.WriteAllText(dump, "existing dump");
            var runner = CreateRunner(testFolder, crashesFolder);

            runner.ClearOutput();

            Assert.True(File.Exists(dump));
            Assert.Contains(runner.GetAvailableCrashesToReport().OfType<ReportableCrashDump>(),
                crash => crash.File == dump);
        }
        finally
        {
            Directory.Delete(testFolder, true);
        }
    }

    private static string CreateTestFolder()
    {
        var testFolder = Path.Combine(Path.GetTempPath(), $"ThriveRunnerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testFolder);
        return testFolder;
    }

    private static ThriveRunner CreateRunner(string installFolder, string crashesFolder)
    {
        var settingsManager = Substitute.For<ILauncherSettingsManager>();
        settingsManager.Settings.Returns(new LauncherSettings());

        var thriveInstaller = Substitute.For<IThriveInstaller>();
        thriveInstaller.BaseInstallFolder.Returns(installFolder);

        var launcherPaths = Substitute.For<ILauncherPaths>();
        launcherPaths.ThriveDefaultCrashesFolder.Returns(crashesFolder);

        return new ThriveRunner(NullLogger<ThriveRunner>.Instance, settingsManager, thriveInstaller,
            Substitute.For<IStoreVersionDetector>(), launcherPaths, Substitute.For<ILauncherOptions>(),
            Substitute.For<ILauncherTranslations>(), Substitute.For<IGpuDeviceEnumerator>());
    }

    private static void SetPrivateProperty(ThriveRunner runner, string propertyName, string value)
    {
        var property = typeof(ThriveRunner).GetProperty(propertyName);
        Assert.NotNull(property);
        property.SetValue(runner, value);
    }
}
