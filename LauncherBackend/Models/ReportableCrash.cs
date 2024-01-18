namespace LauncherBackend.Models;

using Services;
using SharedBase.Utilities;

public abstract class ReportableCrash
{
    public abstract DateTime CrashTime { get; }

    /// <summary>
    ///   True for crash exceptions as they are always from the current run
    /// </summary>
    public abstract bool HappenedNow { get; }

    public abstract string Name { get; }

    public string FormatTime()
    {
        return RecentTimeString.FormatRecentTimeInLocalTime(CrashTime, true,
            LauncherConstants.CrashShortTimeDisplayCutoff);
    }
}

public class ReportableCrashDump : ReportableCrash
{
    public ReportableCrashDump(string file, DateTime? crashTime = null, bool happenedRightNow = false)
    {
        crashTime ??= new FileInfo(file).LastWriteTimeUtc;

        CrashTime = crashTime.Value;
        Name = Path.GetFileName(file);
        File = file;

        HappenedNow = happenedRightNow;
    }

    public override DateTime CrashTime { get; }
    public override bool HappenedNow { get; }
    public override string Name { get; }

    public string File { get; }
}

public class ReportableCrashException : ReportableCrash
{
    public ReportableCrashException(string exception)
    {
        ExceptionCallstack = exception;
    }

    public override DateTime CrashTime => DateTime.UtcNow;
    public override bool HappenedNow => true;
    public override string Name => "Unhandled Exception";

    public string ExceptionCallstack { get; }
}
