using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherBackend.Utilities;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddThriveLauncher(this IServiceCollection serviceCollection)
    {
        return serviceCollection.AddSingleton<ILauncherFeeds, LauncherFeeds>()
            .AddSingleton<IStoreVersionDetector, StoreVersionDetector>()
            .AddSingleton<ILauncherSettingsManager, LauncherSettingsManager>()
            .AddScoped<IHtmlSummaryParser, HtmlSummaryParser>()
            .AddSingleton<ILauncherPaths, LauncherPaths>();
    }
}
