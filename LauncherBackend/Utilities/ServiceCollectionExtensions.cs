namespace LauncherBackend.Utilities;

using Microsoft.Extensions.DependencyInjection;
using Services;

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
