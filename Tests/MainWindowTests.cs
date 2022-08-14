using LauncherBackend.Models;
using LauncherBackend.Services;
using ThriveLauncher.ViewModels;
using Xunit;
using Moq;
using ThriveLauncher.Utilities;

namespace Tests;

public class MainWindowTests
{
    [Fact]
    public void Links_ShowAndCloseWorks()
    {
        var feedsMock = new Mock<ILauncherFeeds>();
        var storeMock = new Mock<IStoreVersionDetector>();
        storeMock.Setup(store => store.Detect()).Returns(new StoreVersionInfo());

        var settingsMock = new Mock<ILauncherSettingsManager>();
        settingsMock.SetupGet(settings => settings.Settings).Returns(new LauncherSettings());

        var viewModel = new MainWindowViewModel(feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities());

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

        var settingsMock = new Mock<ILauncherSettingsManager>();
        settingsMock.SetupGet(settings => settings.Settings).Returns(new LauncherSettings());

        var viewModel = new MainWindowViewModel(feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities());

        Assert.False(viewModel.ShowDevCenterStatusArea);

        storeMock.Verify();

        storeMock = new Mock<IStoreVersionDetector>();
        storeMock.Setup(store => store.Detect())
            .Returns(new StoreVersionInfo());

        viewModel = new MainWindowViewModel(feedsMock.Object, storeMock.Object, settingsMock.Object,
            new VersionUtilities());

        Assert.True(viewModel.ShowDevCenterStatusArea);
    }
}
