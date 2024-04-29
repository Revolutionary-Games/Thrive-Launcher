namespace ThriveLauncher;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Utilities;
using ViewModels;
using Views;

public class App : Application
{
    private readonly IServiceProvider serviceCollection;

    public App(IServiceProvider serviceCollection)
    {
        this.serviceCollection = serviceCollection;
    }

    public override void Initialize()
    {
        // To provide access to any controls to get to the services
        Resources[typeof(IServiceProvider)] = serviceCollection;

        DataTemplates.Add(serviceCollection.GetRequiredService<ViewLocator>());

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SetupMainWindow();

        base.OnFrameworkInitializationCompleted();

        ShowCPUWarningWindowIfRequired();
    }

    /// <summary>
    ///   Called when the launcher backend wants the GUI to be restarted
    /// </summary>
    public void ReSetupMainWindow()
    {
        CreateWindowObject().Show();
    }

    private void SetupMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateWindowObject();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            // TODO: implement
            // ReSharper disable once CommentTypo
            // https://docs.avaloniaui.net/docs/getting-started/application-lifetimes#isingleviewapplicationlifetime
            throw new NotImplementedException();

            // singleView.MainView = new MainView();
        }
    }

    private void ShowCPUWarningWindowIfRequired()
    {
        if (serviceCollection.GetRequiredService<ICPUFeatureCheck>().IsBasicThriveLibrarySupported())
            return;

        var logger = serviceCollection.GetRequiredService<ILogger<App>>();

        logger.LogInformation("Creating and showing CPU warning window");

        var window = new CPUFeatureWarning();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
        {
            // Force the main window visible immediately if not already to make the dialog popup work
            if (!desktop.MainWindow.IsVisible)
            {
                logger.LogInformation("Showing main window early for CPU check warning");
                desktop.MainWindow.Show();
            }

            window.ShowDialog(desktop.MainWindow);
        }
        else
        {
            logger.LogWarning("Cannot show CPU warning as a dialog");
            window.Show();
        }
    }

    private Window CreateWindowObject()
    {
        return new MainWindow
        {
            DataContext = this.CreateInstance<MainWindowViewModel>(),
        };
    }
}
