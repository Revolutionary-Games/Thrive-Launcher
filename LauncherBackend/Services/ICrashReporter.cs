namespace LauncherBackend.Services;

using Models;

public interface ICrashReporter
{
    public long CreatedReportId { get; }
    public string CreatedReportDeleteKey { get; }

    public string? ExtendedErrorMessage { get; }

    public IEnumerable<string> GetAvailableLogFiles();

    public IEnumerable<ReportableCrash> GetAvailableCrashesToReport();

    public Task<CrashReporterSubmitResult> SubmitReport(ReportableCrash crash, IEnumerable<string> logFiles,
        string? extraDescription, string? reporterEmail, bool reportIsPublic, string? launcherSavedOutput);

    public string CreateTextReport(ReportableCrash crash, IEnumerable<string> logFiles, string? extraDescription,
        string? reporterEmail, string? launcherSavedOutput, bool platformSpecificLineEndings);

    public void ClearAllCrashes();
}
