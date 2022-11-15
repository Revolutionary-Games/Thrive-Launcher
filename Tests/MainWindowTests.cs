namespace Tests;

using LauncherBackend.Models;
using LauncherBackend.Services;
using Moq;
using TestUtilities.Utilities;
using ThriveLauncher.ViewModels;
using Utilities;
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
        var viewCreator = new MainWindowViewModelFactory(logger);
        viewCreator.StoreMock.Setup(store => store.Detect()).Returns(new StoreVersionInfo());

        var viewModel = viewCreator.Create();

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
        var viewCreator = new MainWindowViewModelFactory(logger);
        viewCreator.StoreMock.Setup(store => store.Detect())
            .Returns(
                new StoreVersionInfo(StoreVersionInfo.SteamInternalName, "Steam", LauncherConstants.ThriveSteamURL))
            .Verifiable();

        var viewModel = viewCreator.Create();

        Assert.False(viewModel.ShowDevCenterStatusArea);

        viewCreator.StoreMock.Verify();

        viewCreator.StoreMock = new Mock<IStoreVersionDetector>();
        viewCreator.StoreMock.Setup(store => store.Detect())
            .Returns(new StoreVersionInfo());

        viewModel = viewCreator.Create();

        Assert.True(viewModel.ShowDevCenterStatusArea);
    }
}
