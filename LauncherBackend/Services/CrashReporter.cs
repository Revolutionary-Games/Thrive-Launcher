namespace LauncherBackend.Services;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DevCenterCommunication.Forms;
using DevCenterCommunication.Models;
using LauncherThriveShared;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;
using Utilities;

public sealed class CrashReporter : ICrashReporter, IDisposable
{
    private readonly ILogger<CrashReporter> logger;
    private readonly IStoreVersionDetector storeVersionDetector;
    private readonly IThriveRunner thriveRunner;
    private readonly ILauncherPaths launcherPaths;
    private readonly HttpClient httpClient;
    private readonly IReadOnlyList<string> potentialLogFileNames;

    public CrashReporter(ILogger<CrashReporter> logger, IStoreVersionDetector storeVersionDetector,
        IThriveRunner thriveRunner, INetworkDataRetriever networkDataRetriever, ILauncherPaths launcherPaths)
    {
        this.logger = logger;
        this.storeVersionDetector = storeVersionDetector;
        this.thriveRunner = thriveRunner;
        this.launcherPaths = launcherPaths;

        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(1),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(networkDataRetriever.UserAgent);

        potentialLogFileNames = new[] { LauncherConstants.DefaultThriveLogFileName };
    }

    public long CreatedReportId { get; private set; }
    public string CreatedReportDeleteKey { get; private set; } = string.Empty;

    public string? ExtendedErrorMessage { get; private set; }

    public IEnumerable<string> GetAvailableLogFiles()
    {
        var detectedFromOutput = thriveRunner.DetectedFullLogFileLocation;

        if (detectedFromOutput != null && File.Exists(detectedFromOutput))
        {
            logger.LogInformation("Using detected log file from game output at: {DetectedFromOutput}",
                detectedFromOutput);
            return new[] { detectedFromOutput };
        }

        var folder = thriveRunner.DetectedThriveDataFolder;

        if (folder != null)
            folder = Path.Join(folder, ThriveLauncherSharedConstants.LOGS_FOLDER_NAME);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            folder = launcherPaths.ThriveDefaultLogsFolder;

        return GetLogsInFolder(folder);
    }

    public IEnumerable<ReportableCrash> GetAvailableCrashesToReport()
    {
        return thriveRunner.GetAvailableCrashesToReport();
    }

    public async Task<CrashReporterSubmitResult> SubmitReport(ReportableCrash crash, IEnumerable<string> logFiles,
        string? extraDescription, string? reporterEmail, bool reportIsPublic, string? launcherSavedOutput)
    {
        CreatedReportId = -1;
        CreatedReportDeleteKey = string.Empty;
        ExtendedErrorMessage = null;

        var logFileBuilder = PrepareLogFiles(logFiles, launcherSavedOutput);

        var elapsedSinceEpoch = crash.CrashTime - DateTime.UnixEpoch;

        var form = new CreateCrashReportData
        {
            ExitCode = thriveRunner.ExitCode.ToString(),
            CrashTime = (long)Math.Round(elapsedSinceEpoch.TotalSeconds),
            Public = reportIsPublic,
        };

        SetFormFields(extraDescription, reporterEmail, form, logFileBuilder);

        logger.LogDebug("Report form creation succeeded");

        var formData = new MultipartFormDataContent();

        if (crash is ReportableCrashException)
        {
            // TODO: implement crash callstack sending
            throw new NotImplementedException("Sending reports about exceptions is not implemented");
        }

        if (crash is ReportableCrashDump crashDump)
        {
            formData.Add(new StreamContent(File.OpenRead(crashDump.File)), "dump", "dump");
        }
        else
        {
            throw new ArgumentException("Unknown crash type to report");
        }

        foreach (var formProperty in form.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var formValue = formProperty.GetValue(form);

            if (formValue == null)
                continue;

            formData.Add(new StringContent(formValue.ToString() ?? throw new Exception("Null string conversion")),
                formProperty.Name);
        }

        return await SendCrashReport(formData);
    }

    public string CreateTextReport(ReportableCrash crash, IEnumerable<string> logFiles, string? extraDescription,
        string? reporterEmail, string? launcherSavedOutput, bool platformSpecificLineEndings)
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append("Thrive has crashed at ");
        stringBuilder.Append(crash.CrashTime.ToString("O"));

        if (crash.HappenedNow)
            stringBuilder.Append(" (crash happened immediately before creating this report)");

        stringBuilder.Append("\n");

        stringBuilder.Append("Exit code: ");
        stringBuilder.Append(thriveRunner.ExitCode);
        stringBuilder.Append("\n");

        if (!string.IsNullOrEmpty(extraDescription))
        {
            stringBuilder.Append("Description: ");
            stringBuilder.Append(extraDescription);
            stringBuilder.Append("\n");
        }

        if (!string.IsNullOrEmpty(reporterEmail))
        {
            stringBuilder.Append("Reporter's email: ");
            stringBuilder.Append(reporterEmail);
            stringBuilder.Append("\n");
        }

        var logText = PrepareLogFiles(logFiles, launcherSavedOutput);

        stringBuilder.Append(logText);
        stringBuilder.Append("\n");

        if (crash is ReportableCrashDump crashDump)
        {
            stringBuilder.Append("Crash dump is stored in file: ");
            stringBuilder.Append(crashDump.File);
            stringBuilder.Append("\n");
        }
        else if (crash is ReportableCrashException crashException)
        {
            stringBuilder.Append("Crash happened due to an unhandled exception:\n");
            stringBuilder.Append(crashException.ExceptionCallstack);
            stringBuilder.Append("\n");
        }
        else
        {
            stringBuilder.Append("Unknown crash type to provide extra info\n");
        }

        if (platformSpecificLineEndings)
            stringBuilder.MakeLineSeparatorsPlatformSpecific();

        return stringBuilder.ToString();
    }

    public void ClearAllCrashes()
    {
        logger.LogInformation("Clearing all crashes");

        foreach (var crash in GetAvailableCrashesToReport())
        {
            if (crash is ReportableCrashDump crashDump)
            {
                logger.LogInformation("Deleting crash dump: {File}", crashDump.File);

                try
                {
                    File.Delete(crashDump.File);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to delete a crash dump");
                }
            }
        }

        logger.LogDebug("Telling Thrive runner to forget its seen crashes");
        thriveRunner.ClearDetectedCrashes();
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static StringBuilder PrepareLogFiles(IEnumerable<string> logFiles, string? launcherSavedOutput)
    {
        var logFileBuilder = new StringBuilder();

        foreach (var logFile in logFiles)
        {
            int length = 0;
            bool truncated = false;

            logFileBuilder.Append("==== START OF ");
            logFileBuilder.Append(Path.GetFileName(logFile));
            logFileBuilder.Append("====\n");

            foreach (var line in File.ReadLines(logFile, Encoding.UTF8))
            {
                length += line.Length;

                if (length < LauncherConstants.MaxCrashLogFileSize)
                {
                    logFileBuilder.Append(line.TrimEnd());
                    logFileBuilder.Append("\n");
                }
                else if (!truncated)
                {
                    truncated = true;
                }
            }

            logFileBuilder.Append("==== END OF ");
            logFileBuilder.Append(Path.GetFileName(logFile));
            logFileBuilder.Append("====\n");

            if (truncated)
            {
                logFileBuilder.Append("Previous file was truncated due to original length being: ");
                logFileBuilder.Append(length);
                logFileBuilder.Append("\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(launcherSavedOutput))
        {
            logFileBuilder.Append("==== START OF LAUNCHER OUTPUT ====\n");
            logFileBuilder.Append(launcherSavedOutput);
            logFileBuilder.Append("\n==== END OF LAUNCHER OUTPUT ====\n");
        }

        return logFileBuilder;
    }

    private void SetFormFields(string? extraDescription, string? reporterEmail, CreateCrashReportData form,
        StringBuilder logFileBuilder)
    {
        if (OperatingSystem.IsWindows())
        {
            form.Platform = "windows";
        }
        else if (OperatingSystem.IsLinux())
        {
            form.Platform = "linux";
        }
        else if (OperatingSystem.IsMacOS())
        {
            form.Platform = "mac";
        }
        else
        {
            throw new NotImplementedException("Current platform is unknown to report for");
        }

        if (logFileBuilder.Length > 0)
            form.LogFiles = logFileBuilder.ToString();

        if (!string.IsNullOrWhiteSpace(extraDescription))
            form.ExtraDescription = extraDescription;

        if (!string.IsNullOrWhiteSpace(reporterEmail) && reporterEmail.Contains("@"))
            form.Email = reporterEmail;

        var storeVersion = storeVersionDetector.Detect();

        form.GameVersion = "unknown";

        // Don't include the arch in normal versions in the report
        if (thriveRunner.PlayedThriveVersion is PlayableVersion playableVersion)
        {
            form.GameVersion = playableVersion.PlainVersionNumber;
        }
        else if (thriveRunner.PlayedThriveVersion != null)
        {
            form.GameVersion = thriveRunner.PlayedThriveVersion.VersionName;
        }

        // Only override the store name for when actually playing the store version and not an external one
        if (storeVersion.IsStoreVersion && thriveRunner.PlayedThriveVersion is StoreVersion)
        {
            logger.LogInformation("This report is about a game store version");
            form.Store = storeVersion.StoreName;
            form.GameVersion = null;
        }
        else
        {
            logger.LogInformation("The created report has version: {GameVersion}", form.GameVersion);
        }
    }

    private async Task<CrashReporterSubmitResult> SendCrashReport(MultipartFormDataContent content)
    {
        logger.LogDebug("Sending crash report to {Url}", LauncherConstants.DevCenterCrashReportURL);

        try
        {
            var result = await httpClient.PostAsync(LauncherConstants.DevCenterCrashReportURL, content);

            var responseContent = await result.Content.ReadAsStringAsync();

            if (!result.IsSuccessStatusCode)
            {
                logger.LogWarning("Got non-success status code from server: {StatusCode}", result.StatusCode);
                ExtendedErrorMessage = $"Server response: {responseContent.Truncate(100)}";

                if (result.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogInformation("We sent too many crash reports");
                    return CrashReporterSubmitResult.TooManyRequests;
                }

                if (result.StatusCode == HttpStatusCode.BadRequest)
                {
                    logger.LogInformation("We sent some bad info");
                    return CrashReporterSubmitResult.BadRequest;
                }

                return CrashReporterSubmitResult.ServerError;
            }

            var response = JsonSerializer.Deserialize<CreateCrashReportResponse>(responseContent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();

            logger.LogInformation("Creating report {CreatedId} succeeded. Delete key: {DeleteKey}", response.CreatedId,
                response.DeleteKey);

            CreatedReportId = response.CreatedId;
            CreatedReportDeleteKey = response.DeleteKey;
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning(e, "Failed HTTP request to submit a crash report");
            ExtendedErrorMessage = e.ToString();
            return CrashReporterSubmitResult.NetworkError;
        }
        catch (JsonException e)
        {
            logger.LogError(e, "Bad JSON response received from server");
            ExtendedErrorMessage = e.ToString();
            return CrashReporterSubmitResult.UnknownError;
        }

        return CrashReporterSubmitResult.Success;
    }

    private IEnumerable<string> GetLogsInFolder(string folder)
    {
        foreach (var potentialFile in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(potentialFile);

            if (potentialLogFileNames.Any(f => fileName.EndsWith(f)))
            {
                yield return potentialFile;
            }
        }
    }
}
