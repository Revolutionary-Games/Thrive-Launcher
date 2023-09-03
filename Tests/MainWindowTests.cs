namespace Tests;

using System;
using LauncherBackend.Models;
using LauncherBackend.Services;
using NSubstitute;
using TestUtilities.Utilities;
using ThriveLauncher.ViewModels;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class MainWindowTests : IDisposable
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
        viewCreator.StoreMock.Detect().Returns(new StoreVersionInfo());

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

        viewCreator.StoreMock.Received().Detect();
    }

    [Fact]
    public void DevCenter_HiddenForSteamVersion()
    {
        var viewCreator = new MainWindowViewModelFactory(logger);
        viewCreator.StoreMock.Detect()
            .Returns(
                new StoreVersionInfo(StoreVersionInfo.SteamInternalName, "Steam", LauncherConstants.ThriveSteamURL));

        var viewModel = viewCreator.Create();

        Assert.False(viewModel.ShowDevCenterStatusArea);

        viewCreator.StoreMock.Received().Detect();

        viewCreator.StoreMock = Substitute.For<IStoreVersionDetector>();
        viewCreator.StoreMock.Detect().Returns(new StoreVersionInfo());

        viewModel = viewCreator.Create();

        Assert.True(viewModel.ShowDevCenterStatusArea);

        viewCreator.StoreMock.Received().Detect();
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
