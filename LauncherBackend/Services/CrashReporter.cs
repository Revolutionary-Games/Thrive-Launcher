namespace LauncherBackend.Services;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DevCenterCommunication.Forms;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

public class CrashReporter : ICrashReporter
{
    private readonly ILogger<CrashReporter> logger;
    private readonly IStoreVersionDetector storeVersionDetector;
    private readonly IThriveRunner thriveRunner;
    private readonly HttpClient httpClient;
    private readonly IReadOnlyList<string> potentialLogFileNames;

    public CrashReporter(ILogger<CrashReporter> logger, IStoreVersionDetector storeVersionDetector,
        IThriveRunner thriveRunner, INetworkDataRetriever networkDataRetriever)
    {
        this.logger = logger;
        this.storeVersionDetector = storeVersionDetector;
        this.thriveRunner = thriveRunner;

        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(1),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(networkDataRetriever.UserAgent);

        potentialLogFileNames = new[] { LauncherConstants.DefaultThriveLogFileName };
    }

    public long CreatedReportId { get; private set; }
    public string CreatedReportDeleteKey { get; private set; }

    public string? ExtendedErrorMessage { get; private set; }

    public async Task<CrashReporterSubmitResult> SubmitReport(IEnumerable<string> logFiles, string extraDescription,
        string reporterEmail, bool reportIsPublic)
    {
        CreatedReportId = -1;
        CreatedReportDeleteKey = string.Empty;
        ExtendedErrorMessage = null;

        var logFileBuilder = PrepareLogFiles(logFiles);

        DateTime selectedCrashTime;

        var epochTime = selectedCrashTime - DateTime.UnixEpoch;

        var form = new CreateCrashReportData
        {
            ExitCode = thriveRunner.ExitCode.ToString(),
            CrashTime = (long)Math.Round(epochTime.TotalSeconds),
            Public = reportIsPublic,
        };

        SetFormFields(extraDescription, reporterEmail, form, logFileBuilder);

        logger.LogDebug("Report form creation succeeded");

        var formData = new MultipartFormDataContent();

        // TODO: implement crash callstack sending
        string dumpFile;

        formData.Add(new StreamContent(File.OpenRead(dumpFile)), "dump", "dump");

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

    private static StringBuilder PrepareLogFiles(IEnumerable<string> logFiles)
    {
        var logFileBuilder = new StringBuilder();

        foreach (var logFile in logFiles)
        {
            int length = 0;
            bool truncated = true;

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

        return logFileBuilder;
    }

    private void SetFormFields(string extraDescription, string reporterEmail, CreateCrashReportData form,
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

        if (storeVersion.IsStoreVersion)
        {
            form.Store = storeVersion.StoreName;
        }
        else
        {
            form.GameVersion = thriveRunner.PlayedThriveVersion;
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

    private IEnumerable<(string File, DateTime ModifiedAt)> GetCrashDumpsInFolder(string folder)
    {
        var dumps = new List<(string File, DateTime ModifiedAt)>();

        foreach (var dump in Directory.EnumerateFiles(folder, "*.dmp", SearchOption.AllDirectories))
        {
            dumps.Add((dump, new FileInfo(dump).LastWriteTimeUtc));
        }

        return dumps.OrderByDescending(t => t.ModifiedAt);
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

    private string FormatTime(DateTime time)
    {
        // TODO: maybe would be better to use the duration displayer (but it wasn't in the common module yet) here?
        return RecentTimeString.FormatRecentTimeInLocalTime(time, true, TimeSpan.FromHours(12));
    }
}

public interface ICrashReporter
{
    public long CreatedReportId { get; }
    public string CreatedReportDeleteKey { get; }

    public string? ExtendedErrorMessage { get; }

    public Task<CrashReporterSubmitResult> SubmitReport(IEnumerable<string> logFiles);
}
