namespace Tests.Utilities;

using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ThriveLauncher.Models;
using ThriveLauncher.Services;
using ThriveLauncher.Utilities;
using ThriveLauncher.ViewModels;

/// <summary>
///   Helper for creating <see cref="MainWindowViewModel"/> instances
/// </summary>
public class MainWindowViewModelFactory
{
    // This logger is for an object we create
    // ReSharper disable once ContextualLoggerProblem
    public MainWindowViewModelFactory(ILogger<MainWindowViewModel> logger)
    {
        Logger = logger;
        SettingsMock.Settings.Returns(new LauncherSettings());
    }

    public ILauncherFeeds FeedsMock { get; set; } = Substitute.For<ILauncherFeeds>();
    public IStoreVersionDetector StoreMock { get; set; } = Substitute.For<IStoreVersionDetector>();

    public ILauncherPaths PathsMock { get; set; } = Substitute.For<ILauncherPaths>();

    public ILauncherSettingsManager SettingsMock { get; set; } = Substitute.For<ILauncherSettingsManager>();

    public IThriveAndLauncherInfoRetriever InfoRetrieveMock { get; set; } =
        Substitute.For<IThriveAndLauncherInfoRetriever>();

    public IThriveInstaller InstallerMock { get; set; } = Substitute.For<IThriveInstaller>();

    public IDevCenterClient DevCenterMock { get; set; } = Substitute.For<IDevCenterClient>();

    public IThriveRunner ThriveRunnerMock { get; } = Substitute.For<IThriveRunner>();

    public Options LauncherOptions { get; set; } = new();

    public IAutoUpdater AutoUpdaterMock { get; set; } = Substitute.For<IAutoUpdater>();

    public IBackgroundExceptionNoticeDisplayer BackgroundExceptionNoticeDisplayer { get; set; } =
        Substitute.For<IBackgroundExceptionNoticeDisplayer>();

    public ILoggingManager LoggingManagerMock { get; set; } = Substitute.For<ILoggingManager>();

    public ILogger<MainWindowViewModel> Logger { get; }

    public MainWindowViewModel Create()
    {
        return new MainWindowViewModel(Logger, FeedsMock, StoreMock, SettingsMock,
            new VersionUtilities(), PathsMock, InfoRetrieveMock, InstallerMock,
            DevCenterMock, ThriveRunnerMock, LauncherOptions, AutoUpdaterMock,
            BackgroundExceptionNoticeDisplayer, LoggingManagerMock, false);
    }
}
