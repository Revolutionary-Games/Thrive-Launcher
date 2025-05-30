﻿namespace ThriveLauncher;

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
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
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
using Services;
using SharedBase.Utilities;
using Utilities;
using ViewModels;
using LogLevel = NLog.LogLevel;

internal class Program
{
    private static bool registeredCancelPressHandler;
    private static int cancelPressCount;

    private static bool reActivationRequested;

    private static bool launcherQuitRequested;

    // Initialisation code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialised
    // yet, and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var options = new Options();

        var parsed = new Parser(config =>
            {
                config.AllowMultiInstance = true;
                config.AutoHelp = true;
                config.AutoVersion = true;
                config.EnableDashDash = true;
                config.IgnoreUnknownArguments = false;
                config.MaximumDisplayWidth = 80;
                config.HelpWriter = Console.Error;
            }).ParseArguments<Options>(args)
            .WithNotParsed(ErrorOnUnparsed);

        if (parsed.Value != null)
            options = parsed.Value;

        if (!CheckLogLevelOptionIsFine(options))
            return 1;

        // We build services before starting avalonia so that we can use launcher backend services before we decide
        // if we want to fire up ourGUI
        var services = BuildLauncherServices(true, options);

        var programLogger = services.GetRequiredService<ILogger<Program>>();

        // Hold onto a memory-mapped file while we are running to let other programs know we are open
        MemoryMappedFile? globalMemory = null;

        if (!options.SkipGlobalMemory)
            globalMemory = CreateOrOpenMemoryMappedFile(programLogger);

        try
        {
            Trace.Listeners.Clear();
            Trace.Listeners.Add(services.GetRequiredService<AvaloniaLogger>());

            if (options.PrintAvailableLocales)
                PrintAvailableLocales(programLogger);

            return InnerMain(args, services, programLogger);
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

    /// <summary>
    ///   Signals the main loop that the launcher should quit now
    /// </summary>
    public static void OnLauncherWantsToCloseNow()
    {
        launcherQuitRequested = true;
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
            .AddSingleton<IBackgroundExceptionHandler>(sp =>
                sp.GetRequiredService<IBackgroundExceptionNoticeDisplayer>())
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
    private static int InnerMain(string[] args, ServiceProvider services, ILogger programLogger)
    {
        programLogger.LogInformation("Thrive Launcher version {Version} starting",
            services.GetRequiredService<VersionUtilities>().LauncherVersion);

        var options = services.GetRequiredService<Options>();
        var runner = services.GetRequiredService<IThriveRunner>();

        var settingsManager = LoadAndApplySettings(services, programLogger, options, out var settings);

        ApplyLoggingSettings(services, programLogger, settings);

        ApplyWantedLocale(programLogger, settings, options);

        var temporaryFilesHandler = services.GetRequiredService<TemporaryFilesCleaner>();

        // This is fine to allow just run in the background
        services.GetRequiredService<IBackgroundExceptionHandler>().HandleTask(temporaryFilesHandler.Perform());

        var storeVersionInfo = services.GetRequiredService<IStoreVersionDetector>().Detect();
        var isStore = storeVersionInfo.IsStoreVersion;

        bool cpuIsSupported = services.GetRequiredService<ICPUFeatureCheck>().IsBasicThriveLibrarySupported();

        if (!cpuIsSupported)
        {
            programLogger.LogInformation("Current CPU is likely not capable of running Thrive");
        }
        else
        {
            // Store version can run Thrive in seamless mode after which the launcher wants to quit
            // For simplicity this only works if the CPU check warning doesn't need to be shown
            if (HandleStoreVersionLogic(programLogger, isStore, settings, options, settingsManager, storeVersionInfo,
                    runner))
            {
                return 0;
            }
        }

        programLogger.LogInformation("Launcher starting GUI");

        // We can't use StartWithClassicDesktopLifetime as we need control over the lifetime
        var avaloniaBuilder = BuildAvaloniaAppWithServices(services);

        var shutdownTask = Task.Run(async () =>
        {
            try
            {
                await RunCustomShutdownWatcher(avaloniaBuilder, services);
            }
            catch (Exception e)
            {
                programLogger.LogError(e, "Error in task watching when the process should quit");
            }
        });

        programLogger.LogInformation("Start running Avalonia desktop lifetime");
        int exitCode = avaloniaBuilder.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

        if (!launcherQuitRequested)
        {
            shutdownTask.Wait(TimeSpan.FromMilliseconds(700));
        }
        else
        {
            // If the task has likely commanded the lifetime to just quit, give a bit more wait time
            shutdownTask.Wait(TimeSpan.FromMilliseconds(1500));
        }

        if (shutdownTask.IsCompleted)
        {
            programLogger.LogWarning("Main shutdown watcher task has not exited even after a short wait");
        }

        temporaryFilesHandler.OnShutdown();

        if (exitCode == 0)
        {
            programLogger.LogInformation("Launcher process exiting normally");
        }
        else
        {
            programLogger.LogInformation("Launcher process exiting with code {ExitCode}", exitCode);
        }

        return exitCode;
    }

    private static async Task RunCustomShutdownWatcher(AppBuilder avaloniaBuilder, ServiceProvider services)
    {
        var programLogger = services.GetRequiredService<ILogger<Program>>();
        var runner = services.GetRequiredService<IThriveRunner>();

        int failures = 0;

        App applicationInstance;

        // As this task is started before the main application loop starts, we might not have the app created yet, so
        // we need to wait for it
        while (true)
        {
            var readAttempt = (App?)avaloniaBuilder.Instance;

            if (readAttempt != null)
            {
                applicationInstance = readAttempt;
                break;
            }

            await Task.Delay(1);

            ++failures;

            if (failures > 10000)
            {
                programLogger.LogError(
                    "Cannot detect the application instance, shutdown logic will not work correctly");
                failures = 0;
            }
        }

        var lifetime = (ClassicDesktopStyleApplicationLifetime?)applicationInstance.ApplicationLifetime;

        if (lifetime == null)
        {
            programLogger.LogError("Lifetime not found, can't detect when this should shutdown");
            throw new Exception("Lifetime not created");
        }

        // Mac activation / background operation when no windows are open
        bool stayActiveInBackground = false;

        var activatableApplicationLifetime = applicationInstance.TryGetFeature<IActivatableLifetime>();
        if (activatableApplicationLifetime != null)
        {
            stayActiveInBackground = true;
            activatableApplicationLifetime.Activated += OnActivationRequest;
        }

        if (!stayActiveInBackground && OperatingSystem.IsMacOS())
        {
            programLogger.LogWarning("Staying active in background on mac is not working due to no " +
                "compatible lifetime detected");
        }

        // Don't stay open without windows when debugging as that's annoying
#if DEBUG
        if (Debugger.IsAttached)
            stayActiveInBackground = false;
#endif

        // TODO: should the store versions also exclude staying open?

        // How often to check if the program should quit, this should be pretty low but not too low to consume a bunch
        // of extra CPU
        var runInterval = TimeSpan.FromMilliseconds(30);

        failures = 0;
        bool seenInitialWindow = false;
        int windowWaitCount = 0;

        while (true)
        {
            await Task.Delay(runInterval);

            if (launcherQuitRequested)
            {
                programLogger.LogInformation("Launcher quit requested, exiting shutdown watcher loop");
                lifetime.Shutdown();
                break;
            }

            try
            {
                // If there are Avalonia windows open, we don't need to do anything
                if (lifetime.Windows.Count > 0)
                {
                    seenInitialWindow = true;

                    // If the user clicks on the icon while there are open windows, that causes problems when the user
                    // does want to close the window, so reset the request here when there is an open window
                    reActivationRequested = false;

                    failures = 0;
                    continue;
                }
            }
            catch (Exception e)
            {
                if (failures > 10)
                {
                    programLogger.LogError("Exiting due to failing to check open window count too many times");
                    lifetime.Shutdown(4);
                    break;
                }

                programLogger.LogWarning(e, "Couldn't read open window count");
                ++failures;
                continue;
            }

            // Wait for the initial window to open and don't just quit immediately
            if (!seenInitialWindow)
            {
                ++windowWaitCount;

                if (windowWaitCount > 1000)
                {
                    programLogger.LogError("Main window not detected as opened, will quit the launcher to " +
                        "avoid being invisible to the user");
                    lifetime.Shutdown(5);
                    break;
                }

                continue;
            }

            failures = 0;

            bool keepShowingLauncher = false;

            if (runner.ThriveRunning)
            {
                programLogger.LogInformation(
                    "Thrive is currently running, waiting for Thrive to quit before exiting the launcher process");

                if (!await WaitForRunningThriveToExitAsync(runner, programLogger))
                {
                    programLogger.LogInformation(
                        "Thrive didn't quit properly (or wants us to be shown again) while we waited for it, " +
                        "trying to re-show the launcher");
                    keepShowingLauncher = true;
                }
            }

            if (stayActiveInBackground)
            {
                if (reActivationRequested)
                {
                    keepShowingLauncher = true;
                    reActivationRequested = false;
                }
                else
                {
                    // Wait until an external quit signal (or reactivation signal)
                    continue;
                }
            }

            programLogger.LogInformation("Detected no windows open anymore");

            if (keepShowingLauncher)
            {
                programLogger.LogInformation("Recreating main window and showing it again");

                Dispatcher.UIThread.Post(() => applicationInstance.ReSetupMainWindow());

                seenInitialWindow = false;
                windowWaitCount = 0;
            }
            else
            {
                programLogger.LogInformation("Time to exit the process");
                lifetime.Shutdown();
                break;
            }
        }

        programLogger.LogDebug("Exiting program quit handler task");

        if (activatableApplicationLifetime != null)
        {
            activatableApplicationLifetime.Activated -= OnActivationRequest;
        }
    }

    private static void OnActivationRequest(object? sender, ActivatedEventArgs e)
    {
        switch (e.Kind)
        {
            case ActivationKind.OpenUri:
                // TODO: handle this?
                break;
            case ActivationKind.Reopen:
                reActivationRequested = true;
                break;
            case ActivationKind.Background:
                // This seems to be related to the program shutdown on Mac and not about reopening
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static ILauncherSettingsManager LoadAndApplySettings(ServiceProvider services, ILogger programLogger,
        Options options, out LauncherSettings settings)
    {
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
        settings = settingsManager.Settings;
        programLogger.LogDebug("Settings loaded");
        return settingsManager;
    }

    private static void ApplyWantedLocale(ILogger programLogger, LauncherSettings settings, Options options)
    {
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
    }

    private static void ApplyLoggingSettings(ServiceProvider services, ILogger programLogger, LauncherSettings settings)
    {
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
    }

    private static bool HandleStoreVersionLogic(ILogger programLogger, bool isStore, LauncherSettings settings,
        Options options, ILauncherSettingsManager settingsManager, StoreVersionInfo storeVersionInfo,
        IThriveRunner runner)
    {
        if (!isStore)
            return false;

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
                        return true;
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

        return false;
    }

    private static void ErrorOnUnparsed(IEnumerable<Error> errors)
    {
        // Duplicated code from ScriptsBase as this doesn't want to depend on that as that causes a publishing failure
        var errorList = errors.ToList();

        if (errorList.Count < 1)
            return;

        var firstError = errorList.First();

        if (firstError.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError
            or ErrorType.HelpVerbRequestedError)
        {
            Environment.Exit(0);
        }

        Console.Out.WriteLine("Incorrect command line arguments");
        Console.Error.WriteLine("Unknown command line arguments specified: ");

        foreach (var error in errorList)
            Console.Error.WriteLine(error.Tag);

        Console.Error.WriteLine();

        Environment.Exit(1);
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
            Console.Out.WriteLine("Invalid log level specified");

            var logLevels = string.Join(", ", LogLevel.AllLevels.Select(l => l.Name));

            Console.Out.WriteLine($"Available log levels: {logLevels}");
            Environment.Exit(1);
            return false;
        }

        return true;
    }

    private static async Task<bool> WaitForRunningThriveToExitAsync(IThriveRunner runner, ILogger logger)
    {
        SetupCancelPressHandler(runner, logger);

        int waitCounter = 0;

        while (runner.ThriveRunning && cancelPressCount < 3)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            ++waitCounter;

            if (waitCounter > 600)
            {
                waitCounter = 0;
                logger.LogInformation("Still waiting for our child Thrive process to quit...");
            }
        }

        return HandlePostThriveWait(runner);
    }

    private static bool WaitForRunningThriveToExit(IThriveRunner runner, ILogger logger)
    {
        SetupCancelPressHandler(runner, logger);

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

        return HandlePostThriveWait(runner);
    }

    private static bool HandlePostThriveWait(IThriveRunner runner)
    {
        // If cancelled we don't want to even think about showing the launcher again
        if (cancelPressCount > 0)
            return true;

        // Success when no crashes detected (and no problems the user should be advised on), the user didn't
        // explicitly ask to open the launcher, and Thrive started correctly
        return !runner.HasReportableCrash && runner.ActiveErrorSuggestion == null &&
            !runner.ThriveWantsToOpenLauncher && runner.ThriveStartedCorrectly;
    }

    private static void SetupCancelPressHandler(IThriveRunner runner, ILogger logger)
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
            // Skip the default language not meant to be in the list
            if (cultureInfo.Equals(sortingLocale))
                continue;

            message.Append($"\"{cultureInfo.Name}\",\n");
        }

        logger.LogInformation("Available locales:\n{Message}", message.ToString());
    }
}
