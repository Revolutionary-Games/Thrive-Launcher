namespace Tests;

using LauncherBackend.Models;
using LauncherBackend.Services;
using Moq;
using TestUtilities.Utilities;
using ThriveLauncher.Utilities;
using ThriveLauncher.ViewModels;
using Xunit;
using Xunit.Abstractions;

public class MainWindowTests
{
    private readonly XunitLogger<MainWindowViewModel> logger;

    public MainWindowTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<MainWindowViewModel>(output);
    }

    [Fact]
    public void Links_ShowAndCloseWorks()
    {
        var feedsMock = new Mock<ILauncherFeeds>();
        var storeMock = new Mock<IStoreVersionDetector>();
        var pathsMock = new Mock<ILauncherPaths>();
        storeMock.Setup(store => store.Detect()).Returns(new StoreVersionInfo());

        var settingsMock = new Mock<ILauncherSettingsManager>();
        settingsMock.SetupGet(settings => settings.Settings).Returns(new LauncherSettings());

        var viewModel = new MainWindowViewModel(logger, feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities(), pathsMock.Object);

        Assert.False(viewModel.ShowLinksPopup);

        viewModel.ToggleLinksView();

        Assert.True(viewModel.ShowLinksPopup);

        viewModel.ToggleLinksView();

        Assert.False(viewModel.ShowLinksPopup);

        viewModel.ToggleLinksView();

        Assert.True(viewModel.ShowLinksPopup);

        viewModel.CloseLinksClicked();

        Assert.False(viewModel.ShowLinksPopup);
    }

    [Fact]
    public void DevCenter_HiddenForSteamVersion()
    {
        var feedsMock = new Mock<ILauncherFeeds>();
        var storeMock = new Mock<IStoreVersionDetector>();
        storeMock.Setup(store => store.Detect())
            .Returns(new StoreVersionInfo(StoreVersionInfo.SteamInternalName, "Steam")).Verifiable();

        var pathsMock = new Mock<ILauncherPaths>();

        var settingsMock = new Mock<ILauncherSettingsManager>();
        settingsMock.SetupGet(settings => settings.Settings).Returns(new LauncherSettings());

        var viewModel = new MainWindowViewModel(logger, feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities(), pathsMock.Object);

        Assert.False(viewModel.ShowDevCenterStatusArea);

        storeMock.Verify();

        storeMock = new Mock<IStoreVersionDetector>();
        storeMock.Setup(store => store.Detect())
            .Returns(new StoreVersionInfo());

        viewModel = new MainWindowViewModel(logger, feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities(), pathsMock.Object);

        Assert.True(viewModel.ShowDevCenterStatusArea);
    }
}
