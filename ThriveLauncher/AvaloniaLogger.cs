namespace ThriveLauncher;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
///   Adapter to get the Avalonia trace based logging to go to our logging service
/// </summary>
public class AvaloniaLogger : TraceListener
{
    private readonly ILogger<AvaloniaLogger> logger;

    public AvaloniaLogger(ILogger<AvaloniaLogger> logger)
    {
        this.logger = logger;
    }

    public override void Write(string? message)
    {
        logger.LogInformation("{Message}", message);
    }

    public override void WriteLine(string? message)
    {
        logger.LogInformation("{Message}", message);
    }
}
