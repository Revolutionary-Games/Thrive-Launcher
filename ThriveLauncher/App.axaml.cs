namespace ThriveLauncher;

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LauncherBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Localization;
using Utilities;
using ViewModels;
using Views;

public class App : Application
{
    private readonly IServiceProvider serviceCollection;

    private Window? newMainWindow;

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
        newMainWindow = CreateWindowObject();
        newMainWindow.Show();
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
        const int delayAfterActivate = 500;

        var menu = new NativeMenu();

        // TODO: implement reacting to language change
        // Languages.OnLanguageChanged

        var localizer = Localizer.Instance;

        // Play thrive item
        var playThriveMenuItem = new NativeMenuItem(localizer["NativeMenuPlayThrive"])
        {
            Gesture = KeyGesture.Parse("Cmd+P"),
        };
        playThriveMenuItem.Click +=
            (_, _) =>
            {
                // TODO: this technically doesn't require the main window to be visible so we could skip this
                // But for now that is not done as it has more corner cases to deal with
                var (window, delay) = MakeSureMainWindowIsVisible(lifetime);

                if (delay)
                {
                    RunWithDelay(() => { (window.DataContext as MainWindowViewModel)?.TryToPlayThrive(); },
                        TimeSpan.FromMilliseconds(delayAfterActivate));
                }
                else
                {
                    (window.DataContext as MainWindowViewModel)?.TryToPlayThrive();
                }
            };
        menu.Add(playThriveMenuItem);

        // About menu item
        var aboutMenuItem = new NativeMenuItem(localizer["NativeMenuAbout"]);
        aboutMenuItem.Click += (_, _) =>
        {
            var (window, delay) = MakeSureMainWindowIsVisible(lifetime);

            if (delay)
            {
                RunWithDelay(() => { (window.DataContext as MainWindowViewModel)?.ShowAboutPage(); },
                    TimeSpan.FromMilliseconds(delayAfterActivate));
            }
            else
            {
                (window.DataContext as MainWindowViewModel)?.ShowAboutPage();
            }
        };
        menu.Add(aboutMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Preferences menu item
        var preferencesMenuItem = new NativeMenuItem(localizer["NativeMenuPreferences"])
        {
            Gesture = KeyGesture.Parse("Cmd+OemComma"),
        };
        preferencesMenuItem.Click += (_, _) =>
        {
            var (window, delay) = MakeSureMainWindowIsVisible(lifetime);

            if (delay)
            {
                RunWithDelay(() => { (window.DataContext as MainWindowViewModel)?.OpenSettingsWithoutToggle(); },
                    TimeSpan.FromMilliseconds(delayAfterActivate));
            }
            else
            {
                (window.DataContext as MainWindowViewModel)?.OpenSettingsWithoutToggle();
            }
        };
        menu.Add(preferencesMenuItem);

        // Set the menu
        NativeMenu.SetMenu(this, menu);
    }

    private async void RunWithDelay(Action action, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            action.Invoke();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private (Window Window, bool ReCreated) MakeSureMainWindowIsVisible(
        IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var window = newMainWindow ?? lifetime.MainWindow;
        bool created = false;

        // Apparently the only way to check if the window can be still opened is if the platform impl still exists
        if (window?.PlatformImpl == null)
        {
            ReSetupMainWindow();
            window = newMainWindow ?? throw new InvalidOperationException("MainWindow is null");
            created = true;
        }
        else
        {
            window.Show();
        }

        window.Activate();
        return (window, created);
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
