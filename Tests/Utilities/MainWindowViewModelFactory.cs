namespace Tests.Utilities;

using LauncherBackend.Models;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using Moq;
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
        SettingsMock.SetupGet(settings => settings.Settings).Returns(new LauncherSettings());
    }

    public Mock<ILauncherFeeds> FeedsMock { get; set; } = new();
    public Mock<IStoreVersionDetector> StoreMock { get; set; } = new();

    public Mock<ILauncherPaths> PathsMock { get; set; } = new();

    public Mock<ILauncherSettingsManager> SettingsMock { get; set; } = new();

    public Mock<IThriveAndLauncherInfoRetriever> InfoRetrieveMock { get; set; } = new();

    public ILogger<MainWindowViewModel> Logger { get; }

    public MainWindowViewModel Create()
    {
        return new MainWindowViewModel(Logger, FeedsMock.Object, StoreMock.Object, SettingsMock.Object,
            new VersionUtilities(), PathsMock.Object, InfoRetrieveMock.Object, false);
    }
}
