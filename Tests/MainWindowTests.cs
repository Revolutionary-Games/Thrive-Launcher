using ThriveLauncher.ViewModels;
using Xunit;

namespace Tests;

public class MainWindowTests
{
    [Fact]
    public void Links_ShowAndCloseWorks()
    {
        var viewModel = new MainWindowViewModel();

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
}
