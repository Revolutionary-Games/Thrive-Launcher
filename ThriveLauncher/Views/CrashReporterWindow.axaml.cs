namespace ThriveLauncher.Views;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LauncherBackend.Models;
using ReactiveUI;
using Services.Localization;
using ViewModels;

public partial class CrashReporterWindow : Window
{
    private readonly Dictionary<string, CheckBox> fileSelectionCheckBoxes = new();

    private bool dataContextReceived;

    private bool autoChangingCheckBoxes;

    public CrashReporterWindow()
    {
        InitializeComponent();

        DataContextProperty.Changed.Subscribe(OnDataContextReceiver);
    }

    private CrashReporterWindowViewModel DerivedDataContext =>
        (CrashReporterWindowViewModel?)DataContext ?? throw new Exception("DataContext not initialized");

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (dataContextReceived && (DerivedDataContext.RequireCloseConfirmationIfCloseAttempted &&
                !DerivedDataContext.WantsReporterClosed))
        {
            // Cancel closing and show the confirmation popup instead
            e.Cancel = true;
            DerivedDataContext.ShowCloseConfirmation = true;
        }
    }

    private void OnDataContextReceiver(AvaloniaPropertyChangedEventArgs e)
    {
        if (dataContextReceived || e.NewValue == null)
            return;

        // Prevents recursive calls
        dataContextReceived = true;

        var dataContext = DerivedDataContext;

        dataContext.WhenAnyValue(d => d.WantsReporterClosed).Subscribe(OnWindowWantsToClose);

        dataContext.WhenAnyValue(d => d.AvailableCrashesToPick).Subscribe(OnAvailableCrashesToReportChanged);

        dataContext.WhenAnyValue(d => d.AvailableLogFilesToAttach).Subscribe(OnAvailableLogFilesToIncludeChanged);
        dataContext.WhenAnyValue(d => d.SelectedLogFilesToAttach).Subscribe(OnSelectedLogFilesChanged);

        // Unlike the main view we kick things off in this window like this
        dataContext.DetectAvailableCrashes();
    }

    private void CancelReporting(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        DerivedDataContext.WantsReporterClosed = true;
    }

    private void OnWindowWantsToClose(bool close)
    {
        if (!close)
            return;

        Close();
    }

    private async void CopyDeleteLinkToClipboard(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var clipboard = Application.Current?.Clipboard;

        if (clipboard == null)
            throw new InvalidOperationException("Clipboard doesn't exist");

        await clipboard.SetTextAsync(DerivedDataContext.CreatedReportDeleteUrl);
    }

    private void OnAvailableCrashesToReportChanged(List<ReportableCrash> crashes)
    {
        AvailableCrashesToReportList.Children.Clear();

        var now = Properties.Resources.TimeMomentRightNow;
        var atTemplate = Properties.Resources.AtTime;

        var linkClasses = new Classes("TextLink");

        foreach (var crash in crashes)
        {
            var container = new WrapPanel();

            container.Children.Add(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = crash.HappenedNow ? now : string.Format(atTemplate, crash.FormatTime()),
                Margin = new Thickness(3, 0, 5, 0),
            });

            var nameLink = new Button
            {
                Classes = linkClasses,
                Content = new TextBlock
                {
                    Text = crash.Name,

                    // FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            container.Children.Add(nameLink);

            nameLink.Click += (_, _) => DerivedDataContext.OnCrashSelectedToReport(crash);

            AvailableCrashesToReportList.Children.Add(container);
        }
    }

    private void OnAvailableLogFilesToIncludeChanged(List<string> logFiles)
    {
        LogFilesToIncludeContainer.Children.Clear();
        fileSelectionCheckBoxes.Clear();

        var linkClasses = new Classes("TextLink");

        // See LocalizeExtension
        var openFolderBinding = new Binding($"[{nameof(Properties.Resources.OpenFolder)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        var includeBinding = new Binding($"[{nameof(Properties.Resources.IncludeInReport)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        foreach (var logFile in logFiles)
        {
            var container = new WrapPanel();

            var checkBox = new CheckBox
            {
                [!ContentProperty] = includeBinding,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };

            checkBox.Checked += (_, _) =>
            {
                if (!autoChangingCheckBoxes)
                    DerivedDataContext.ToggleLogInclusion(logFile, true);
            };
            checkBox.Unchecked += (_, _) =>
            {
                if (!autoChangingCheckBoxes)
                    DerivedDataContext.ToggleLogInclusion(logFile, false);
            };

            container.Children.Add(checkBox);
            fileSelectionCheckBoxes[logFile] = checkBox;

            var nameLink = new Button
            {
                Classes = linkClasses,
                Content = new TextBlock
                {
                    Text = Path.GetFileName(logFile),
                    TextWrapping = TextWrapping.Wrap,
                },
                Margin = new Thickness(0, 0, 8, 0),
            };

            nameLink.Click += (_, _) => DerivedDataContext.OpenLogFile(logFile);
            container.Children.Add(nameLink);

            var folderButton = new Button
            {
                [!ContentProperty] = openFolderBinding,
                VerticalAlignment = VerticalAlignment.Center,
            };

            folderButton.Click += (_, _) => DerivedDataContext.OpenFileFolder(logFile);
            container.Children.Add(folderButton);

            LogFilesToIncludeContainer.Children.Add(container);
        }

        // Just in case these are triggered in the wrong order, call this here
        OnSelectedLogFilesChanged(DerivedDataContext.SelectedLogFilesToAttach);
    }

    private void OnSelectedLogFilesChanged(List<string> selectedFiles)
    {
        autoChangingCheckBoxes = true;

        foreach (var (key, checkBox) in fileSelectionCheckBoxes)
        {
            checkBox.IsChecked = selectedFiles.Contains(key);
        }

        autoChangingCheckBoxes = false;
    }
}
