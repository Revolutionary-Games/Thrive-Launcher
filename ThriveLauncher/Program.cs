namespace ThriveLauncher;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using CommandLine;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using Properties;
using ScriptsBase.Utilities;
using Services;
using SharedBase.Utilities;
using Utilities;
using ViewModels;

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
            .AddSingleton<VersionUtilities>()
            .AddSingleton<INetworkDataRetriever, NetworkDataRetriever>()
            .AddSingleton<ILauncherOptions>(options)
            .AddSingleton(options)
            .AddScoped<MainWindowViewModel>()
            .AddScoped<LicensesWindowViewModel>()
            .AddSingleton<ViewLocator>()
            .AddSingleton<ILauncherTranslations, LauncherTranslationProxy>()
            .AddScoped<IExternalTools, ExternalTools>();

        if (normalLogging)
        {
            bool verbose = options.Verbose == true;

            builder = builder.AddLogging(config =>
                {
                    config.ClearProviders();
                    config.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Debug);
                    config.AddNLog(GetNLogConfiguration(true, verbose));
                })
                .AddScoped<AvaloniaLogger>();
        }
        else
        {
            // Design time logging
            builder = builder.AddLogging(config =>
            {
                config.SetMinimumLevel(LogLevel.Debug);
                config.AddNLog(GetNLogConfiguration(false, false));
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

        var options = services.GetRequiredService<Options>();
        var runner = services.GetRequiredService<IThriveRunner>();

        if (options.Verbose == true)
            programLogger.LogDebug("Verbose logging is enabled");

        programLogger.LogDebug("Loading settings");
        var settingsManager = services.GetRequiredService<ILauncherSettingsManager>();
        var settings = settingsManager.Settings;
        programLogger.LogDebug("Settings loaded");

        if (!string.IsNullOrEmpty(settings.SelectedLauncherLanguage) || !string.IsNullOrEmpty(options.Language))
        {
            var language = settings.SelectedLauncherLanguage;

            // Command line language overrides launcher configured language
            if (string.IsNullOrEmpty(language) || !string.IsNullOrEmpty(options.Language))
            {
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

        programLogger.LogInformation("Launcher language is: {CurrentCulture}", CultureInfo.CurrentCulture);

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

                        if (string.IsNullOrWhiteSpace(rememberedVersion) ||
                            rememberedVersion == storeVersion.VersionName)
                        {
                            programLogger.LogInformation("Trying to start game in seamless mode...");
                            runner.LaunchedInSeamlessMode = true;
                            runner.StartThrive(storeVersion, CancellationToken.None);
                            started = true;
                        }
                        else
                        {
                            programLogger.LogInformation(
                                "Seamless mode is disabled due to selected game version in the launcher");
                        }
                    }
                    catch (Exception e)
                    {
                        programLogger.LogError(e, "Failed to run in seamless mode");
                    }

                    // If we failed to start, then fallback to normal launcher operation (so only check running status
                    // if we actually got to start Thrive)
                    if (started)
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
        var applicationInstance = (App)avaloniaBuilder.Instance;

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

    private static LoggingConfiguration GetNLogConfiguration(bool fileLogging, bool verbose)
    {
        // For debugging logging itself
        // InternalLogger.LogLevel = LogLevel.Trace;
        // InternalLogger.LogToConsole = true;

        var configuration = new LoggingConfiguration();

        // TODO: allow configuring the logging level
        configuration.AddRule(verbose ? NLog.LogLevel.Debug : NLog.LogLevel.Info, NLog.LogLevel.Fatal,
            new ConsoleTarget("console"));

        if (Debugger.IsAttached)
            configuration.AddRule(verbose ? NLog.LogLevel.Trace : NLog.LogLevel.Debug, NLog.LogLevel.Fatal,
                new DebuggerTarget("debugger"));

        if (fileLogging)
        {
            var paths = new LauncherPaths(new ConsoleLogger<LauncherPaths>());

            var basePath = "${basedir}/logs";

            try
            {
                Directory.CreateDirectory(paths.PathToLogFolder);
                basePath = paths.PathToLogFolder;
            }
            catch (Exception e)
            {
                Console.WriteLine(Resources.LogFolderCreateFailed, paths.PathToLogFolder, e);
            }

            if (basePath.EndsWith("/"))
                basePath = basePath.Substring(0, basePath.Length - 1);

            var fileTarget = new FileTarget("file")
            {
                // TODO: detect the launcher folder we should put the logs folder in
                FileName = $"{basePath}/thrive-launcher-log.txt",
                ArchiveAboveSize = GlobalConstants.MEBIBYTE * 2,
                ArchiveEvery = FileArchivePeriod.Month,
                ArchiveFileName = $"{basePath}/thrive-launcher-log.{{#}}.txt",
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                ArchiveDateFormat = "yyyy-MM-dd",
                MaxArchiveFiles = 4,
                Encoding = Encoding.UTF8,
                KeepFileOpen = true,
                ConcurrentWrites = true,

                // Use default because people will use notepad on Windows to open the logs and copy a mess
                LineEnding = LineEndingMode.Default,
            };

            configuration.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);
        }

        return configuration;
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

            if (waitCounter > 300)
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
