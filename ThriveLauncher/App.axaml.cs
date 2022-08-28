using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ThriveLauncher.Utilities;
using ThriveLauncher.ViewModels;
using ThriveLauncher.Views;

namespace ThriveLauncher;

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

        // https://github.com/AvaloniaUI/Avalonia/issues/8640
        // Animation.RegisterAnimator<TransformAnimator>(prop => typeof(ITransform).IsAssignableFrom(prop.PropertyType));

        DataTemplates.Add(serviceCollection.GetRequiredService<ViewLocator>());

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = this.CreateInstance<MainWindowViewModel>(),
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            // TODO: implement
            // ReSharper disable once CommentTypo
            // https://docs.avaloniaui.net/docs/getting-started/application-lifetimes#isingleviewapplicationlifetime
            throw new NotImplementedException();

            // singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
