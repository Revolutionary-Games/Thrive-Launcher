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
            .AddScoped<IRehydrator, Rehydrator>()
            .AddScoped<ICrashReporter, CrashReporter>()
            .AddSingleton<ILauncherPaths, LauncherPaths>()
            .AddSingleton<IThriveAndLauncherInfoRetriever, ThriveAndLauncherInfoRetriever>()
            .AddSingleton<IThriveInstaller, ThriveInstaller>()
            .AddSingleton<IDevCenterClient, DevCenterClient>()
            .AddSingleton<IThriveRunner, ThriveRunner>();
    }
}
