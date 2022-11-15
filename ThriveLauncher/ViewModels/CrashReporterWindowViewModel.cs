namespace ThriveLauncher.ViewModels;

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Utilities;

public class CrashReporterWindowViewModel : ViewModelBase
{
    private readonly ILogger<CrashReporterWindowViewModel> logger;
    private readonly ICrashReporter crashReporter;

    private bool showCrashPicker = true;

    private bool requireCloseConfirmationIfCloseAttempted;
    private bool wantsReporterClosed;
    private bool showCloseConfirmation;

    private bool acceptedReportCreationTerms;
    private bool submittingReport;
    private bool showRetryButton;
    private bool reportSubmitted;

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

    public bool ShowCrashPicker
    {
        get => showCrashPicker;
        set => this.RaiseAndSetIfChanged(ref showCrashPicker, value);
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

    public void ClearAllCrashes()
    {
        throw new NotImplementedException();
    }

    // TODO: parameter describing the selected report
    public void OnCrashSelectedToReport()
    {
        RequireCloseConfirmationIfCloseAttempted = true;
        AcceptedReportCreationTerms = false;
        ShowRetryButton = false;

        throw new NotImplementedException();
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
            result = await crashReporter.SubmitReport();
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
