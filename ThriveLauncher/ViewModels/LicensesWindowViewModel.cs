namespace ThriveLauncher.ViewModels;

using System.Reflection;
using System.Threading.Tasks;
using LauncherBackend.Services;
using LauncherBackend.Utilities;

public class LicensesWindowViewModel : ViewModelBase
{
    private readonly Assembly assembly;

    public LicensesWindowViewModel()
    {
        assembly = Assembly.GetExecutingAssembly();
    }

    public Task<string> LauncherLicenseText => ResourceUtilities.ReadManifestResourceAsync("LICENSE.md", assembly);

    public Task<string> RobotoFontLicenseText =>
        ResourceUtilities.ReadManifestResourceAsync("Roboto/LICENSE.txt", assembly);

    public Task<string> OFLLicenseText => ResourceUtilities.ReadManifestResourceAsync("OFL.txt", assembly);

    public void OpenLauncherSourceCode()
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.LauncherRepoURL);
    }
}
