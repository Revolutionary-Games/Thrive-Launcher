namespace ThriveLauncher;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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

        // Set up the native menu early if on macOS
        if (OperatingSystem.IsMacOS() && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            SetupNativeMenu(lifetime);
        }
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

    private void SetupNativeMenu(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var menu = new NativeMenu();

        // About menu item
        var aboutMenuItem = new NativeMenuItem("About ThriveLauncher");
        aboutMenuItem.Click += (_, _) => (lifetime.MainWindow?.DataContext as MainWindowViewModel)?.ShowAboutPage();
        menu.Add(aboutMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Preferences menu item
        var preferencesMenuItem = new NativeMenuItem("Preferences...")
        {
            Gesture = KeyGesture.Parse("Cmd+OemComma"),
        };
        preferencesMenuItem.Click += (_, _) =>
            (lifetime.MainWindow?.DataContext as MainWindowViewModel)?.OpenSettingsWithoutToggle();
        menu.Add(preferencesMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Services submenu
        var servicesMenu = new NativeMenu();
        var playThriveMenuItem = new NativeMenuItem("Play Thrive")
        {
            Gesture = KeyGesture.Parse("Cmd+P"),
        };
        playThriveMenuItem.Click +=
            (_, _) => (lifetime.MainWindow?.DataContext as MainWindowViewModel)?.TryToPlayThrive();
        servicesMenu.Add(playThriveMenuItem);

        var servicesMenuItem = new NativeMenuItem("Services")
        {
            Menu = servicesMenu,
        };
        menu.Add(servicesMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Hide app
        var hideMenuItem = new NativeMenuItem("Hide Thrive Launcher")
        {
            Gesture = KeyGesture.Parse("Cmd+H"),
        };

        hideMenuItem.Click += (_, _) =>
        {
            foreach (var window in lifetime.Windows)
                window.Hide();
        };

        menu.Add(hideMenuItem);

        // Hide others
        var hideOthersMenuItem = new NativeMenuItem("Hide Others")
        {
            Gesture = KeyGesture.Parse("Alt+Cmd+H"),
        };
        menu.Add(hideOthersMenuItem);

        // Show all
        var showAllItem = new NativeMenuItem("Show All");

        // TODO: test is this needed
        /*showAllItem.Click += (_, _) =>
        {
            foreach (var window in lifetime.Windows)
                window.Show();
        };*/

        menu.Add(showAllItem);

        menu.Add(new NativeMenuItemSeparator());

        // Exit
        var exitMenuItem = new NativeMenuItem("Quit ThriveLauncher")
        {
            Gesture = KeyGesture.Parse("Cmd+Q"),
        };
        exitMenuItem.Click += (_, _) => lifetime.Shutdown();
        menu.Add(exitMenuItem);

        // Set the menu
        NativeMenu.SetMenu(this, menu);
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
