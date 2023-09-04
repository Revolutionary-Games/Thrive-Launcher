namespace ThriveLauncher;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using CommandLine;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using NLog.Extensions.Logging;
using Properties;
using ScriptsBase.Utilities;
using Services;
using SharedBase.Utilities;
using Utilities;
using ViewModels;
using LogLevel = NLog.LogLevel;

internal class Program
{
    private static bool registeredCancelPressHandler;
    private static int cancelPressCount;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var options = new Options();

        var parsed = CommandLineHelpers.CreateParser()
            .ParseArguments<Options>(args)
            .WithNotParsed(CommandLineHelpers.ErrorOnUnparsed);

        if (parsed.Value != null)
            options = parsed.Value;

        if (!CheckLogLevelOptionIsFine(options))
            return;

        // We build services before starting avalonia so that we can use launcher backend services before we decide
        // if we want to fire up ourGUI
        var services = BuildLauncherServices(true, options);

        var programLogger = services.GetRequiredService<ILogger<Program>>();

        // Hold onto a memory mapped file while we are running to let other programs know we are open
        MemoryMappedFile? globalMemory = null;

        if (!options.SkipGlobalMemory)
            globalMemory = CreateOrOpenMemoryMappedFile(programLogger);

        try
        {
            Trace.Listeners.Clear();
            Trace.Listeners.Add(services.GetRequiredService<AvaloniaLogger>());

            if (options.PrintAvailableLocales)
                PrintAvailableLocales(programLogger);

            InnerMain(args, services, programLogger);
        }
        catch (Exception e)
        {
            programLogger.LogCritical(e, "Unhandled exception in the launcher. PLEASE REPORT THIS TO US!");

            // Just in case exiting with an exception doesn't save logs correctly, save them explicitly here
            (services.GetService<ILoggerProvider>() as NLogLoggerProvider)?.LogFactory.Flush();

            // TODO: we should show a popup window or something showing the error

            throw;
        }
        finally
        {
            globalMemory?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    // Can't be made private without breaking the designer
    // ReSharper disable once MemberCanBePrivate.Global
    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaAppWithServices(BuildLauncherServices(false, new Options()));
    }

    public static ServiceProvider BuildLauncherServices(bool normalLogging, Options options)
    {
        var builder = new ServiceCollection()
            .AddThriveLauncher()
            .AddSingleton<ILoggingManager, LoggingManager>()
            .AddSingleton<VersionUtilities>()
            .AddSingleton<INetworkDataRetriever, NetworkDataRetriever>()
            .AddSingleton<ILauncherOptions>(options)
            .AddSingleton(options)
            .AddScoped<MainWindowViewModel>()
            .AddScoped<LicensesWindowViewModel>()
            .AddSingleton<ViewLocator>()
            .AddSingleton<ILauncherTranslations, LauncherTranslationProxy>()
            .AddSingleton<IBackgroundExceptionNoticeDisplayer, BackgroundExceptionHandler>()
            .AddSingleton<IBackgroundExceptionHandler>(
                sp => sp.GetRequiredService<IBackgroundExceptionNoticeDisplayer>())
            .AddScoped<IExternalTools, ExternalTools>();

        bool verboseState = options.Verbose == true;
        LogLevel normalLogLevel = LogLevel.FromString(options.LogLevel);

        // Before we create logging, we need to create a special paths object with a custom logger to get past this
        // circular dependency
        var pathWithManualLogging = new LauncherPaths(new ConsoleLogger<LauncherPaths>
        {
            LogLevel = (Microsoft.Extensions.Logging.LogLevel)LoggingManager.GetLogLevel(verboseState, normalLogLevel)
                .Ordinal,
        });

        if (normalLogging)
        {
            builder = builder.AddLogging(config =>
                {
                    config.ClearProviders();

                    // Always passing everything from Microsoft logging may have a tiny performance penalty, but we can
                    // simplify things a lot with this
                    config.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    config.AddNLog(LoggingManager.GetNLogConfiguration(true, verboseState, normalLogLevel,
                        pathWithManualLogging));
                })
                .AddScoped<AvaloniaLogger>();
        }
        else
        {
            verboseState = false;
            normalLogLevel = LogLevel.Info;

            // Design time logging
            builder = builder.AddLogging(config =>
            {
                config.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                config.AddNLog(LoggingManager.GetNLogConfiguration(false, verboseState, normalLogLevel,
                    pathWithManualLogging));
            });
        }

        var services = builder.BuildServiceProvider();

        var logManager = services.GetRequiredService<ILoggingManager>();
        logManager.SetDefaultOptions(verboseState, normalLogLevel);

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

        var options = services.GetRequiredService<Options>();
        var runner = services.GetRequiredService<IThriveRunner>();

        if (options.Verbose == true)
            programLogger.LogDebug("Verbose logging is enabled");

        programLogger.LogDebug("Current process culture (language) is: {CurrentCulture}", CultureInfo.CurrentCulture);

        var uiCulture = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;

        if (!Equals(uiCulture, CultureInfo.CurrentCulture))
        {
            programLogger.LogWarning("Startup current culture doesn't match UI culture ({UICulture})", uiCulture);
        }

        programLogger.LogDebug("Startup launcher language is: {StartupLanguage}", Languages.GetStartupLanguage());

        programLogger.LogDebug("Loading settings");
        var settingsManager = services.GetRequiredService<ILauncherSettingsManager>();
        var settings = settingsManager.Settings;
        programLogger.LogDebug("Settings loaded");

        var logManager = services.GetRequiredService<ILoggingManager>();

        if (settings.VerboseLogging)
        {
            programLogger.LogInformation(
                "Enabling verbose logging based on saved launcher settings, note that to get all startup logging " +
                "in verbose mode a command line flag is required");
            logManager.ApplyVerbosityOption(true);
        }
        else
        {
            // The verbosity apply will log the new settings so that ends with two prints if we did this anyway, that's
            // why this is inside an if
            logManager.LogLoggingOptions();
        }

        if (!string.IsNullOrEmpty(settings.SelectedLauncherLanguage) || !string.IsNullOrEmpty(options.Language))
        {
            var language = settings.SelectedLauncherLanguage;

            // Command line language overrides launcher configured language
            if (string.IsNullOrEmpty(language) || !string.IsNullOrEmpty(options.Language))
            {
                programLogger.LogInformation("Using command line defined language: {Language}", options.Language);

                try
                {
                    language = new CultureInfo(options.Language!).NativeName;
                }
                catch (Exception e)
                {
                    programLogger.LogError(e, "Command line specified language is incorrect (format example: en-GB)");
                }
            }

            programLogger.LogInformation("Applying configured language: {Language}", language);

            try
            {
                Languages.SetLanguage(language!);
            }
            catch (Exception e)
            {
                programLogger.LogError(e, "Failed to apply configured language, using default");
                programLogger.LogInformation("Available languages: {Languages}",
                    Languages.GetLanguagesEnumerable().Select(l => l.Name));
            }
        }
        else
        {
            var availableCultures = Languages.GetAvailableLanguages();
            var currentlyUsed = Languages.GetCurrentlyUsedCulture(availableCultures);

            programLogger.LogInformation(
                "No language selected, making sure default is applied. Detected default language: {Name}",
                currentlyUsed.Name);

            Languages.SetLanguage(currentlyUsed);
        }

        programLogger.LogInformation("Launcher language (culture) is: {CurrentCulture}", CultureInfo.CurrentCulture);

        var storeVersionInfo = services.GetRequiredService<IStoreVersionDetector>().Detect();
        var isStore = storeVersionInfo.IsStoreVersion;

        if (isStore)
        {
            programLogger.LogInformation("This is the store version of the launcher");

            if (settings.EnableStoreVersionSeamlessMode)
            {
                if (!options.AllowSeamlessMode || options.DisableSeamlessMode)
                {
                    programLogger.LogInformation("Seamless launcher mode is disabled by command line options");
                }
                else
                {
                    programLogger.LogInformation(
                        "Using seamless launcher mode, will attempt to launch before initializing GUI");

                    // If we failed to start, then fallback to normal launcher operation (so only check running status
                    // if we actually got to start Thrive)
                    if (TryStartSeamlessMode(programLogger, settingsManager, storeVersionInfo, runner))
                    {
                        if (WaitForRunningThriveToExit(runner, programLogger))
                        {
                            programLogger.LogInformation("Exiting directly after playing Thrive in seamless mode, " +
                                "as launcher doesn't want to be shown");
                            return;
                        }

                        programLogger.LogInformation("Launcher wants to be shown after Thrive run in seamless mode");
                        runner.LaunchedInSeamlessMode = false;
                    }
                }
            }
            else
            {
                programLogger.LogInformation(
                    "Seamless launcher mode is disabled due to the launcher options being turned off by the user");
            }
        }

        programLogger.LogInformation("Launcher starting GUI");

        // Very important to use our existing services to configure the Avalonia app here, otherwise everything
        // will break
        bool keepShowingLauncher;

        // We can't use StartWithClassicDesktopLifetime as we need control over the lifetime
        var avaloniaBuilder = BuildAvaloniaAppWithServices(services);

        using var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose,
        };
        avaloniaBuilder.SetupWithLifetime(lifetime);
        var applicationInstance = (App?)avaloniaBuilder.Instance ?? throw new Exception("Application not found");

        // This loop is here so that we can restart the avalonia GUI to show Thrive run errors and provide crash
        // reporting
        do
        {
            keepShowingLauncher = false;

            programLogger.LogInformation("Start running Avalonia desktop lifetime");
            lifetime.Start(args);

            if (runner.ThriveRunning)
            {
                programLogger.LogInformation(
                    "Thrive is currently running, waiting for Thrive to quit before exiting the launcher process");

                if (!WaitForRunningThriveToExit(runner, programLogger))
                {
                    programLogger.LogInformation(
                        "Thrive didn't quit properly while we waited for it, trying to re-show the launcher");
                    keepShowingLauncher = true;
                }
            }

            if (keepShowingLauncher)
            {
                programLogger.LogInformation("Recreating main window to prepare it to be shown again");
                applicationInstance.ReSetupMainWindow();
            }
        }
        while (keepShowingLauncher);

        programLogger.LogInformation("Launcher process exiting normally");
    }

    private static AppBuilder BuildAvaloniaAppWithServices(IServiceProvider serviceProvider)
    {
        return AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
    }

    private static bool CheckLogLevelOptionIsFine(Options options)
    {
        try
        {
            LogLevel.FromString(options.LogLevel);
        }
        catch (ArgumentException)
        {
            ColourConsole.WriteErrorLine("Invalid log level specified");

            var logLevels = string.Join(", ", LogLevel.AllLevels.Select(l => l.Name));

            ColourConsole.WriteNormalLine($"Available log levels: {logLevels}");
            Environment.Exit(1);
            return false;
        }

        return true;
    }

    private static bool WaitForRunningThriveToExit(IThriveRunner runner, ILogger logger)
    {
        cancelPressCount = 0;

        if (!registeredCancelPressHandler)
        {
            registeredCancelPressHandler = true;
            Console.CancelKeyPress += (_, args) =>
            {
                logger.LogInformation("Got cancellation request, trying to close Thrive");
                ++cancelPressCount;

                if (!runner.QuitThrive())
                {
                    logger.LogInformation("Could not signal Thrive process to quit");
                }
                else
                {
                    logger.LogInformation("Thrive runner was signaled to stop");
                }

                if (cancelPressCount > 3)
                {
                    logger.LogInformation("Got so many cancellation requests that waiting will be canceled");
                }

                // Cancel terminating the current program until someone really mashes things on the keyboard
                if (cancelPressCount < 5)
                {
                    args.Cancel = true;
                }
            };
        }

        int waitCounter = 0;

        while (runner.ThriveRunning && cancelPressCount < 3)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            ++waitCounter;

            if (waitCounter > 600)
            {
                waitCounter = 0;
                logger.LogInformation("Still waiting for our child Thrive process to quit...");
            }
        }

        // If cancelled we don't want to even think about showing the launcher again
        if (cancelPressCount > 0)
            return true;

        // Success when no crashes detected (and no problems the user should be advised on) and the user didn't
        // explicitly ask to open the launcher
        return !runner.HasReportableCrash && runner.ActiveErrorSuggestion == null && !runner.ThriveWantsToOpenLauncher;
    }

    private static MemoryMappedFile? CreateOrOpenMemoryMappedFile(ILogger logger)
    {
        try
        {
            // This is only currently done (and needed on windows)
            if (!OperatingSystem.IsWindows())
                return null;

            return MemoryMappedFile.CreateNew(LauncherConstants.LauncherGlobalMemoryMapName, 256);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not open global memory mapped file to mark ourselves as running. " +
                "This may indicate the launcher is already open");
            return null;
        }
    }

    private static bool TryStartSeamlessMode(ILogger programLogger, ILauncherSettingsManager settingsManager,
        StoreVersionInfo storeVersionInfo, IThriveRunner runner)
    {
        Task<DownloadableInfo> DummyGetMethod(DevBuildLauncherDTO build)
        {
            _ = build;
            throw new NotSupportedException("Dummy method was called");
        }

        bool started = false;

        try
        {
            // Seamless mode should only trigger when remembered version is null or the store version
            // otherwise we'd need to do complex stuff like potentially waiting for a
            // DevCenter connection here
            var rememberedVersion = settingsManager.RememberedVersion;

            programLogger.LogDebug("Remembered version is: {RememberedVersion}", rememberedVersion);

            // If the store version starts using translations in ThriveInstaller.GetAvailableThriveVersions
            // then this should be updated as well, though not the most critical thing as remembered
            // version being null should still allow things to work
            var storeVersion = storeVersionInfo.CreateStoreVersion();

            var devBuildVersions = new List<IPlayableVersion>
            {
                new DevBuildVersion(PlayableDevCenterBuildType.DevBuild, DummyGetMethod),
                new DevBuildVersion(PlayableDevCenterBuildType.PublicBuildA, DummyGetMethod),
                new DevBuildVersion(PlayableDevCenterBuildType.PublicBuildB, DummyGetMethod),
                new DevBuildVersion(PlayableDevCenterBuildType.PublicBuildC, DummyGetMethod),
            };

            bool attemptStart = false;

            // Seamless mode is allowed when no remembered version exists, or the remembered version is invalid
            // (for example due to external versions being disabled)
            if (string.IsNullOrWhiteSpace(rememberedVersion))
            {
                attemptStart = true;
            }
            else if (rememberedVersion == storeVersion.VersionName)
            {
                attemptStart = true;
            }
            else if (devBuildVersions.Any(b => b.VersionName == rememberedVersion))
            {
                // Don't attempt for DevBuilds
                attemptStart = false;
            }
            else if (!settingsManager.Settings.StoreVersionShowExternalVersions)
            {
                // External version showing is disabled so even if we remember that, we couldn't run it so we can use
                // seamless mode
                attemptStart = true;
            }

            if (attemptStart)
            {
                programLogger.LogInformation("Trying to start game in seamless mode...");
                runner.LaunchedInSeamlessMode = true;

                // No extra cancellation token is passed as we will use the cancel in WaitForRunningThriveToExit
                runner.StartThrive(storeVersion, true, CancellationToken.None);
                started = true;
            }
            else
            {
                programLogger.LogInformation(
                    "Seamless mode is disabled due to selected game version ({RememberedVersion})in the launcher",
                    rememberedVersion);
            }
        }
        catch (Exception e)
        {
            programLogger.LogError(e, "Failed to run in seamless mode");
        }

        return started;
    }

    private static void PrintAvailableLocales(ILogger logger)
    {
        var message = new StringBuilder();

        var sortingLocale = Languages.GetDefaultLanguage();

        foreach (var cultureInfo in Languages.GetLanguagesEnumerable()
                     .OrderBy(l => l.NativeName, StringComparer.Create(sortingLocale, true)))
        {
            message.Append($"\"{cultureInfo.Name}\",\n");
        }

        logger.LogInformation("Available locales:\n{Message}", message.ToString());
    }
}
