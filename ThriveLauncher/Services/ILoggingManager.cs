namespace ThriveLauncher.Services;

using NLog;

/// <summary>
///   Helper for applying logging settings and changes
/// </summary>
public interface ILoggingManager
{
    public void ApplyVerbosityOption(bool enabled);

    /// <summary>
    ///   Store the default values of logging levels for when verbose mode is disabled to be restored
    /// </summary>
    /// <param name="verboseEnabled">Command line verbose flag</param>
    /// <param name="logLevel">The log level</param>
    public void SetDefaultOptions(bool verboseEnabled, LogLevel logLevel);

    public void LogLoggingOptions();
}
