using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using SharedBase.Utilities;
using ThriveLauncher.Properties;
using ThriveLauncher.Utilities;
using ThriveLauncher.ViewModels;
using LogLevel = NLog.LogLevel;

namespace ThriveLauncher
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // TODO: handle normal flags like "-v" and "-h" here

            // We build services before starting avalonia so that we can use launcher backend services before we decide
            // if we want to fire up ourGUI
            var services = BuildLauncherServices(true);

            var programLogger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                Trace.Listeners.Clear();
                Trace.Listeners.Add(services.GetRequiredService<AvaloniaLogger>());

                InnerMain(args, services, programLogger);
            }
            catch (Exception e)
            {
                programLogger.LogCritical(e, "Unhandled exception in the launcher. PLEASE REPORT THIS TO US!");

                // Just in case exiting with an exception doesn't save logs correctly, save them explicitly here
                (services.GetService<ILoggerProvider>() as NLogLoggerProvider)?.LogFactory.Flush();

                throw;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        // Can't be made private without breaking the designer
        // ReSharper disable once MemberCanBePrivate.Global
        public static AppBuilder BuildAvaloniaApp()
            => BuildAvaloniaAppWithServices(BuildLauncherServices(false));

        public static ServiceProvider BuildLauncherServices(bool normalLogging)
        {
            var builder = new ServiceCollection()
                .AddThriveLauncher()
                .AddSingleton<VersionUtilities>()
                .AddSingleton<INetworkDataRetriever, NetworkDataRetriever>()
                .AddScoped<MainWindowViewModel>()
                .AddScoped<LicensesWindowViewModel>()
                .AddSingleton<ViewLocator>();

            if (normalLogging)
            {
                builder = builder.AddLogging(config =>
                    {
                        config.ClearProviders();
                        config.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                        config.AddNLog(GetNLogConfiguration(true));
                    })
                    .AddScoped<AvaloniaLogger>();
            }
            else
            {
                // Design time logging
                builder = builder.AddLogging(config =>
                {
                    config.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                    config.AddNLog(GetNLogConfiguration(false));
                });
            }

            var services = builder.BuildServiceProvider();

            return services;
        }

        /// <summary>
        ///   Separate actual "logic" of the main method to make it easier to protect against unhandled exceptions
        /// </summary>
        /// <param name="args">The program args</param>
        /// <param name="services">The already configured launcher services</param>
        /// <param name="programLogger">Logger for the main method</param>
        private static void InnerMain(string[] args, ServiceProvider services, ILogger programLogger)
        {
            programLogger.LogInformation("Thrive Launcher version {Version} starting",
                services.GetRequiredService<VersionUtilities>().LauncherVersion);

            var settings = services.GetRequiredService<ILauncherSettingsManager>().Settings;

            if (!string.IsNullOrEmpty(settings.SelectedLauncherLanguage))
            {
                programLogger.LogInformation("Applying configured language: {SelectedLauncherLanguage}",
                    settings.SelectedLauncherLanguage);

                try
                {
                    Languages.SetLanguage(settings.SelectedLauncherLanguage);
                }
                catch (Exception e)
                {
                    programLogger.LogError(e, "Failed to apply configured language, using default");
                }
            }

            programLogger.LogInformation("Launcher language is: {CurrentCulture}", CultureInfo.CurrentCulture);

            var isStore = services.GetRequiredService<IStoreVersionDetector>().Detect().IsStoreVersion;

            if (isStore)
            {
                programLogger.LogInformation("This is the store version of the launcher");

                if (settings.AutoStartStoreVersion)
                {
                    // TODO: skip with a special flag

                    programLogger.LogInformation(
                        "Using transparent launcher mode, will attempt to launch before initializing GUI");

                    // TODO: handle transparent mode
                }
            }

            programLogger.LogInformation("Launcher starting GUI");

            // Very important to use our existing services to configure the Avalonia app here, otherwise everything
            // will break
            BuildAvaloniaAppWithServices(services).StartWithClassicDesktopLifetime(args);

            programLogger.LogInformation("Launcher process exiting normally");
        }

        private static AppBuilder BuildAvaloniaAppWithServices(IServiceProvider serviceProvider)
            => AppBuilder.Configure(() => new App(serviceProvider))
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();

        private static LoggingConfiguration GetNLogConfiguration(bool fileLogging)
        {
            // For debugging logging
            // InternalLogger.LogLevel = LogLevel.Trace;
            // InternalLogger.LogToConsole = true;

            var configuration = new LoggingConfiguration();

            // TODO: allow configuring the logging level
            configuration.AddRule(LogLevel.Info, LogLevel.Fatal, new ConsoleTarget("console"));

            if (Debugger.IsAttached)
                configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, new DebuggerTarget("debugger"));

            if (fileLogging)
            {
                var fileTarget = new FileTarget("file")
                {
                    // TODO: detect the launcher folder we should put the logs folder in
                    FileName = "${basedir}/logs/thrive-launcher-log.txt",
                    ArchiveAboveSize = GlobalConstants.MEBIBYTE * 2,
                    ArchiveEvery = FileArchivePeriod.Month,
                    ArchiveFileName = "${basedir}/logs/thrive-launcher-log.{#}.txt",
                    ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                    ArchiveDateFormat = "yyyy-MM-dd",
                    MaxArchiveFiles = 4,
                    Encoding = Encoding.UTF8,
                    KeepFileOpen = true,
                    ConcurrentWrites = true,

                    // TODO: should we use default instead?
                    LineEnding = LineEndingMode.LF,
                };

                configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
            }

            return configuration;
        }
    }
}
