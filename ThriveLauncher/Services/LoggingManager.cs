namespace ThriveLauncher.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LauncherBackend.Services;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using Properties;
using SharedBase.Utilities;
using LogLevel = NLog.LogLevel;

public class LoggingManager : ILoggingManager
{
    private readonly ILogger<LoggingManager> logger;
    private readonly ILauncherPaths launcherPaths;
    private readonly NLogLoggerProvider provider;

    private bool verboseEnabled;
    private bool defaultVerbose;
    private LogLevel defaultLevel = LogLevel.Info;

    public LoggingManager(ILogger<LoggingManager> logger, ILoggerProvider provider, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.provider = provider as NLogLoggerProvider ?? throw new ArgumentException("This only works with NLog");
        this.launcherPaths = launcherPaths;
    }

    public static LoggingConfiguration GetNLogConfiguration(bool fileLogging, bool verbose,
        LogLevel baseLogLevel, ILauncherPaths launcherPaths)
    {
        // For debugging logging itself
        // InternalLogger.LogLevel = LogLevel.Trace;
        // InternalLogger.LogToConsole = true;

        var configuration = new LoggingConfiguration();

        configuration.AddRule(GetLogLevel(verbose, baseLogLevel), LogLevel.Fatal,
            new ConsoleTarget("console"));

        if (Debugger.IsAttached)
        {
            configuration.AddRule(GetTraceLogLevel(verbose, baseLogLevel),
                LogLevel.Fatal, new DebuggerTarget("debugger"));
        }

        if (fileLogging)
        {
            var basePath = "${basedir}/logs";

            try
            {
                Directory.CreateDirectory(launcherPaths.PathToLogFolder);
                basePath = launcherPaths.PathToLogFolder;
            }
            catch (Exception e)
            {
                Console.WriteLine(Resources.LogFolderCreateFailed, launcherPaths.PathToLogFolder, e);
            }

            if (basePath.EndsWith("/"))
                basePath = basePath.Substring(0, basePath.Length - 1);

            var fileTarget = new AtomicFileTarget
            {
                FileName = $"{basePath}/thrive-launcher-log.txt",
                ArchiveAboveSize = GlobalConstants.MEBIBYTE * 2,
                ArchiveEvery = FileArchivePeriod.Month,

                // Required to be specified for the archive suffix to work...
                ArchiveFileName = $"{basePath}/thrive-launcher-log.txt",
                ArchiveSuffixFormat = ".{1:yyyy-MM-dd}",
                MaxArchiveFiles = 4,
                Encoding = Encoding.UTF8,
                KeepFileOpen = true,
                ConcurrentWrites = true,

                // Use default because people will use notepad on Windows to open the logs and copy a mess
                LineEnding = LineEndingMode.Default,
            };

            configuration.AddRule(GetLogLevel(verbose, baseLogLevel), LogLevel.Fatal, fileTarget);
        }

        return configuration;
    }

    public static LogLevel GetLogLevel(bool verbose, LogLevel baseLevel)
    {
        // Map log levels higher if the verbose flag is set
        if (verbose)
        {
            if (baseLevel == LogLevel.Debug)
            {
                baseLevel = LogLevel.Trace;
            }
            else if (baseLevel == LogLevel.Info)
            {
                baseLevel = LogLevel.Debug;
            }
            else if (baseLevel == LogLevel.Warn)
            {
                baseLevel = LogLevel.Info;
            }
            else if (baseLevel == LogLevel.Error)
            {
                baseLevel = LogLevel.Warn;
            }

            if (baseLevel == LogLevel.Fatal)
            {
                baseLevel = LogLevel.Error;
            }
        }

        return baseLevel;
    }

    public void ApplyVerbosityOption(bool enabled)
    {
        if (verboseEnabled == enabled)
            return;

        verboseEnabled = enabled;

        var effectiveVerbose = verboseEnabled || defaultVerbose;

        provider.LogFactory.Configuration =
            GetNLogConfiguration(true, effectiveVerbose, defaultLevel, launcherPaths);

        logger.LogInformation("Reconfigured logging with verbose: {EffectiveVerbose} and level: {DefaultLevel} " +
            "(without verbosity adjustment)", effectiveVerbose, defaultLevel);
        LogLoggingOptions();
    }

    public void SetDefaultOptions(bool verbose, LogLevel logLevel)
    {
        defaultVerbose = verbose;
        defaultLevel = logLevel;
    }

    public void LogLoggingOptions()
    {
        logger.LogInformation("Logging level: {Level}", GetLogLevel(verboseEnabled || defaultVerbose, defaultLevel));
    }

    private static LogLevel GetTraceLogLevel(bool verbose, LogLevel baseLevel)
    {
        baseLevel = GetLogLevel(verbose, baseLevel);

        return baseLevel == LogLevel.Debug ? LogLevel.Trace : baseLevel;
    }
}
