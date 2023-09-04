namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using SharedBase.Utilities;
using Utilities;

public class CrashReporterWindowViewModel : ViewModelBase
{
    private readonly ILogger<CrashReporterWindowViewModel> logger;
    private readonly ICrashReporter crashReporter;
    private readonly IBackgroundExceptionHandler backgroundExceptionHandler;

    private bool showCrashPicker = true;

    private List<ReportableCrash> availableCrashesToPick = new();
    private List<string> availableLogFilesToAttach = new();

    /// <summary>
    ///   Selected logs from the list in availableLogFilesToAttach. Don't modify this list as this is not cloned from
    ///   the data source and can be the same object.
    /// </summary>
    private List<string> selectedLogFilesToAttach = new();

    private bool attachLauncherOutput = true;
    private ReportableCrash? selectedCrashToReport;

    private bool requireCloseConfirmationIfCloseAttempted;
    private bool wantsReporterClosed;
    private bool showCloseConfirmation;
    private bool canClearDumps;

    private string userEnteredReportDescription = string.Empty;
    private string reporterEmail = string.Empty;
    private bool reportIsPublic = true;

    private bool acceptedReportCreationTerms;
    private bool submittingReport;
    private bool showRetryButton;
    private bool reportSubmitted;
    private bool deleteDumpAfterReporting;
    private string reportSubmitError = string.Empty;

    private int? autoCloseDelay;

    public CrashReporterWindowViewModel(ILogger<CrashReporterWindowViewModel> logger, ICrashReporter crashReporter,
        IBackgroundExceptionHandler backgroundExceptionHandler)
    {
        this.logger = logger;
        this.crashReporter = crashReporter;
        this.backgroundExceptionHandler = backgroundExceptionHandler;
    }

    /// <summary>
    ///   Constructor for live preview
    /// </summary>
    public CrashReporterWindowViewModel() : this(
        DesignTimeServices.Services.GetRequiredService<ILogger<CrashReporterWindowViewModel>>(),
        DesignTimeServices.Services.GetRequiredService<ICrashReporter>(),
        DesignTimeServices.Services.GetRequiredService<IBackgroundExceptionHandler>())
    {
    }

    public bool RequireCloseConfirmationIfCloseAttempted
    {
        get => requireCloseConfirmationIfCloseAttempted;
        private set => this.RaiseAndSetIfChanged(ref requireCloseConfirmationIfCloseAttempted, value);
    }

    public bool WantsReporterClosed
    {
        get => wantsReporterClosed;
        set => this.RaiseAndSetIfChanged(ref wantsReporterClosed, value);
    }

    public bool ShowCloseConfirmation
    {
        get => showCloseConfirmation;
        set => this.RaiseAndSetIfChanged(ref showCloseConfirmation, value);
    }

    public bool CanClearDumps
    {
        get => canClearDumps;
        set => this.RaiseAndSetIfChanged(ref canClearDumps, value);
    }

    public bool ShowCrashPicker
    {
        get => showCrashPicker;
        set => this.RaiseAndSetIfChanged(ref showCrashPicker, value);
    }

    public List<ReportableCrash> AvailableCrashesToPick
    {
        get => availableCrashesToPick;
        set => this.RaiseAndSetIfChanged(ref availableCrashesToPick, value);
    }

    public List<string> AvailableLogFilesToAttach
    {
        get => availableLogFilesToAttach;
        set => this.RaiseAndSetIfChanged(ref availableLogFilesToAttach, value);
    }

    public List<string> SelectedLogFilesToAttach
    {
        get => selectedLogFilesToAttach;
        set => this.RaiseAndSetIfChanged(ref selectedLogFilesToAttach, value);
    }

    /// <summary>
    ///   When true the launcher's output window contents are attached to the report
    /// </summary>
    public bool AttachLauncherOutput
    {
        get => attachLauncherOutput;
        set => this.RaiseAndSetIfChanged(ref attachLauncherOutput, value);
    }

    // TODO: allow the user to edit this with a popup window
    public string? LauncherOutputText { get; set; }

    public string UserEnteredReportDescription
    {
        get => userEnteredReportDescription;
        set => this.RaiseAndSetIfChanged(ref userEnteredReportDescription, value);
    }

    public string ReporterEmail
    {
        get => reporterEmail;
        set => this.RaiseAndSetIfChanged(ref reporterEmail, value);
    }

    public bool ReportIsPublic
    {
        get => reportIsPublic;
        set => this.RaiseAndSetIfChanged(ref reportIsPublic, value);
    }

    public bool AcceptedReportCreationTerms
    {
        get => acceptedReportCreationTerms;
        set
        {
            if (value == acceptedReportCreationTerms)
                return;

            this.RaiseAndSetIfChanged(ref acceptedReportCreationTerms, value);
            this.RaisePropertyChanged(nameof(CanSubmitReport));
        }
    }

    public bool DeleteCrashDumpAfterReporting
    {
        get => deleteDumpAfterReporting;
        set => this.RaiseAndSetIfChanged(ref deleteDumpAfterReporting, value);
    }

    public ReportableCrash? SelectedCrashToReport
    {
        get => selectedCrashToReport;
        set
        {
            if (value == selectedCrashToReport)
                return;

            this.RaiseAndSetIfChanged(ref selectedCrashToReport, value);
            this.RaisePropertyChanged(nameof(SelectedCrashName));
            this.RaisePropertyChanged(nameof(ShowCrashDumpDeleteAfterReportCheckBox));
            this.RaisePropertyChanged(nameof(ReportingCrashInfoString));
            this.RaisePropertyChanged(nameof(CrashReportIsOld));
            this.RaisePropertyChanged(nameof(CrashReportIsForException));
        }
    }

    public string? SelectedCrashName => SelectedCrashToReport?.Name;
    public bool ShowCrashDumpDeleteAfterReportCheckBox => SelectedCrashToReport is ReportableCrashDump;

    public bool CrashReportIsForException => SelectedCrashToReport is ReportableCrashException;

    public bool CrashReportIsOld
    {
        get
        {
            if (SelectedCrashToReport == null)
                return false;

            return DateTime.UtcNow - SelectedCrashToReport.CrashTime >
                LauncherConstants.OldCrashReportWarningThreshold;
        }
    }

    public string ReportingCrashInfoString => string.Format(Resources.ActiveReportingInfo, SelectedCrashName,
        SelectedCrashToReport?.FormatTime());

    public bool SubmittingReport
    {
        get => submittingReport;
        set => this.RaiseAndSetIfChanged(ref submittingReport, value);
    }

    public string ReportSubmitError
    {
        get => reportSubmitError;
        set => this.RaiseAndSetIfChanged(ref reportSubmitError, value);
    }

    public bool ShowRetryButton
    {
        get => showRetryButton;
        set => this.RaiseAndSetIfChanged(ref showRetryButton, value);
    }

    public bool CanSubmitReport => AcceptedReportCreationTerms;

    public bool ReportSubmitted
    {
        get => reportSubmitted;
        set => this.RaiseAndSetIfChanged(ref reportSubmitted, value);
    }

    /// <summary>
    ///   A delay in seconds when this window will automatically request to be closed
    /// </summary>
    public int? AutoCloseDelay
    {
        get => autoCloseDelay;
        set
        {
            if (value == autoCloseDelay)
                return;

            this.RaiseAndSetIfChanged(ref autoCloseDelay, value);
            this.RaisePropertyChanged(nameof(AutoCloseDelayText));
        }
    }

    public string? AutoCloseDelayText
    {
        get
        {
            if (autoCloseDelay == null)
                return null;

            if (autoCloseDelay == 1)
                return Resources.SecondsDisplaySingular;

            return string.Format(Resources.SecondsDisplayPlural, autoCloseDelay);
        }
    }

    public string CreatedReportViewUrl =>
        $"{LauncherConstants.DevCenterCrashReportInfoPrefix}{crashReporter.CreatedReportId}";

    public string CreatedReportDeleteUrl =>
        $"{LauncherConstants.DevCenterCrashReportDeletePrefix}{crashReporter.CreatedReportDeleteKey}";

    public void DismissCloseConfirmation()
    {
        ShowCloseConfirmation = false;
    }

    public void DetectAvailableCrashes()
    {
        AvailableCrashesToPick = crashReporter.GetAvailableCrashesToReport().ToList();

        CanClearDumps = AvailableCrashesToPick.Count > 0;
    }

    public void ClearAllCrashes()
    {
        crashReporter.ClearAllCrashes();

        // We no longer want the window to be shown
        logger.LogInformation("Requesting reporter close after clearing dumps");
        WantsReporterClosed = true;
    }

    public void OnCrashSelectedToReport(ReportableCrash crash)
    {
        RequireCloseConfirmationIfCloseAttempted = true;
        AcceptedReportCreationTerms = false;
        ShowRetryButton = false;
        AttachLauncherOutput = true;
        DeleteCrashDumpAfterReporting = true;
        ReportSubmitError = string.Empty;

        AvailableLogFilesToAttach = crashReporter.GetAvailableLogFiles().ToList();

        // TODO: if not selecting the latest crash, this shouldn't auto select the logs

        // ToList doesn't need to be called here as the selected logs are not modified
        SelectedLogFilesToAttach = AvailableLogFilesToAttach;

        UserEnteredReportDescription = string.Empty;
        ReporterEmail = string.Empty;
        ReportIsPublic = true;

        SelectedCrashToReport = crash;
        ShowCrashPicker = false;
    }

    public void ToggleLogInclusion(string logFile, bool shouldBeIncluded)
    {
        logger.LogDebug("Log inclusion set to {ShouldBeIncluded}", shouldBeIncluded);

        if (!AvailableLogFilesToAttach.Contains(logFile))
            throw new ArgumentException("Unknown log file");

        var existingStatus = SelectedLogFilesToAttach.Contains(logFile);

        if (existingStatus == shouldBeIncluded)
        {
            logger.LogDebug("Log file {LogFile} already in right status", logFile);
            return;
        }

        if (shouldBeIncluded)
        {
            SelectedLogFilesToAttach = SelectedLogFilesToAttach.Append(logFile).ToList();
        }
        else
        {
            SelectedLogFilesToAttach = SelectedLogFilesToAttach.Where(l => l != logFile).ToList();
        }
    }

    public void OpenLogFile(string logFile)
    {
        logger.LogInformation("Attempting to open log file: {LogFile}", logFile);
        FileUtilities.OpenFileOrFolderInDefaultProgram(logFile);
    }

    public void OpenFileFolder(string logFile)
    {
        var folder = Path.GetDirectoryName(logFile) ??
            throw new ArgumentException("Could not get folder for log file");

        logger.LogInformation("Attempting to open folder {Folder}", folder);

        FileUtilities.OpenFileOrFolderInDefaultProgram(folder);
    }

    public void CancelCrashReporting()
    {
        ShowCrashPicker = true;
        RequireCloseConfirmationIfCloseAttempted = false;
    }

    public void SubmitReport()
    {
        if (SubmittingReport)
        {
            logger.LogWarning("Already submitting a report, not doing a duplicate submit");
            return;
        }

        if (!CanSubmitReport)
        {
            logger.LogError("Trying to submit a report with incorrect data");
            return;
        }

        logger.LogInformation("Starting report submit");
        SubmittingReport = true;

        backgroundExceptionHandler.HandleTask(DoReportSubmit());
    }

    public void CancelAutoClose()
    {
        AutoCloseDelay = null;
    }

    public void OpenViewUrl()
    {
        logger.LogInformation("Opening report view url: {CreatedReportViewUrl}", CreatedReportViewUrl);
        URLUtilities.OpenURLInBrowser(CreatedReportViewUrl);
    }

    public void OpenDeleteUrl()
    {
        logger.LogInformation("Opening report DELETE url: {CreatedReportDeleteUrl}", CreatedReportDeleteUrl);
        URLUtilities.OpenURLInBrowser(CreatedReportDeleteUrl);
    }

    public string GetReportToCopyToClipboard()
    {
        if (SelectedCrashToReport == null)
            throw new InvalidOperationException("No crash report is selected");

        return crashReporter.CreateTextReport(SelectedCrashToReport, new string[] { },
            CrashReportIsForException ? "Crash is an unhandled exception" : string.Empty, null, LauncherOutputText,
            true);
    }

    private async Task DoReportSubmit()
    {
        CrashReporterSubmitResult result;
        try
        {
            var launcherOutput = AttachLauncherOutput ? LauncherOutputText : null;

            if (string.IsNullOrWhiteSpace(launcherOutput))
                launcherOutput = null;

            result = await crashReporter.SubmitReport(SelectedCrashToReport ?? throw new Exception("No crash selected"),
                SelectedLogFilesToAttach, UserEnteredReportDescription, ReporterEmail, ReportIsPublic, launcherOutput);
        }
        catch (Exception e)
        {
            result = CrashReporterSubmitResult.UnknownError;
            logger.LogError(e, "Failed to submit a report due to exception");
        }

        Dispatcher.UIThread.Post(() =>
        {
            logger.LogInformation("Finished submitting a report (or it failed)");

            if (result != CrashReporterSubmitResult.Success)
            {
                logger.LogWarning("Failed to submit a report with error type: {Result}", result);

                ReportSubmitError = result switch
                {
                    CrashReporterSubmitResult.TooManyRequests => Resources.ReportSubmitErrorTooManyRequests,
                    CrashReporterSubmitResult.ServerError => Resources.ReportSubmitErrorServerError,
                    CrashReporterSubmitResult.BadRequest => Resources.ReportSubmitErrorBadRequest,
                    CrashReporterSubmitResult.NetworkError => Resources.ReportSubmitErrorNetworkError,
                    CrashReporterSubmitResult.UnknownError => Resources.ReportSubmitErrorUnknown,
                    _ => Resources.ReportSubmitErrorUnknown,
                };

                ShowRetryButton = true;
            }
            else
            {
                RequireCloseConfirmationIfCloseAttempted = false;

                this.RaisePropertyChanged(nameof(CreatedReportViewUrl));
                this.RaisePropertyChanged(nameof(CreatedReportDeleteUrl));
                ReportSubmitted = true;
                ReportSubmitError = string.Empty;

                if (DeleteCrashDumpAfterReporting && SelectedCrashToReport is ReportableCrashDump crashDump)
                {
                    logger.LogInformation("Deleting reported crash dump: {File}", crashDump.File);

                    try
                    {
                        File.Delete(crashDump.File);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to delete crash dump file");
                    }
                }

                AutoCloseDelay = 45;
                backgroundExceptionHandler.HandleTask(CloseWindowWithDelay());
            }

            SubmittingReport = false;
        });
    }

    private async Task CloseWindowWithDelay()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            var delay = AutoCloseDelay;

            if (delay == null)
            {
                // Canceled
                logger.LogInformation("Auto close canceled");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (delay <= 0)
                {
                    logger.LogInformation("Auto close delay expired, closing");
                    WantsReporterClosed = true;
                    return;
                }

                AutoCloseDelay = delay.Value - 1;
            });
        }
    }
}
