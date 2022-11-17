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
using Utilities;

public class CrashReporterWindowViewModel : ViewModelBase
{
    private readonly ILogger<CrashReporterWindowViewModel> logger;
    private readonly ICrashReporter crashReporter;

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

    public string userEnteredReportDescription = string.Empty;
    public string reporterEmail = string.Empty;
    public bool reportIsPublic = true;

    private bool acceptedReportCreationTerms;
    private bool submittingReport;
    private bool showRetryButton;
    private bool reportSubmitted;
    private bool deleteDumpAfterReporting;

    private int? autoCloseDelay;

    public CrashReporterWindowViewModel(ILogger<CrashReporterWindowViewModel> logger, ICrashReporter crashReporter)
    {
        this.logger = logger;
        this.crashReporter = crashReporter;
    }

    /// <summary>
    ///   Constructor for live preview
    /// </summary>
    public CrashReporterWindowViewModel() : this(
        DesignTimeServices.Services.GetRequiredService<ILogger<CrashReporterWindowViewModel>>(),
        DesignTimeServices.Services.GetRequiredService<ICrashReporter>())
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
        }
    }

    public string? SelectedCrashName => SelectedCrashToReport?.Name;
    public bool ShowCrashDumpDeleteAfterReportCheckBox => SelectedCrashToReport is ReportableCrashDump;

    public string ReportingCrashInfoString => string.Format(Resources.ActiveReportingInfo, SelectedCrashName,
        SelectedCrashToReport?.FormatTime());

    public bool SubmittingReport
    {
        get => submittingReport;
        set => this.RaiseAndSetIfChanged(ref submittingReport, value);
    }

    // TODO: implement
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
        URLUtilities.OpenLocalFileOrFolder(logFile);
    }

    public void OpenFileFolder(string logFile)
    {
        var folder = Path.GetDirectoryName(logFile) ??
            throw new ArgumentException("Could not get folder for log file");

        logger.LogInformation("Attempting to open folder {Folder}", folder);

        URLUtilities.OpenLocalFileOrFolder(folder);
    }

    public void CancelCrashReporting()
    {
        ShowCrashPicker = true;
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

        Task.Run(DoReportSubmit);
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

                // TODO: show error

                ShowRetryButton = true;
            }
            else
            {
                RequireCloseConfirmationIfCloseAttempted = false;

                this.RaisePropertyChanged(nameof(CreatedReportViewUrl));
                this.RaisePropertyChanged(nameof(CreatedReportDeleteUrl));
                ReportSubmitted = true;

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
                Task.Run(CloseWindowWithDelay);
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

            if (delay <= 0)
            {
                logger.LogInformation("Auto close delay expired, closing");
                WantsReporterClosed = true;
                return;
            }

            // TODO: make sure this doesn't cause problems if the user manually closes the window
            AutoCloseDelay = delay.Value - 1;
        }
    }
}
