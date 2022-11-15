namespace ThriveLauncher.Views;

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using ViewModels;

public partial class CrashReporterWindow : Window
{
    private bool dataContextReceived;

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
}
