namespace ThriveLauncher;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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

    private Window CreateWindowObject()
    {
        return new MainWindow
        {
            DataContext = this.CreateInstance<MainWindowViewModel>(),
        };
    }
}
