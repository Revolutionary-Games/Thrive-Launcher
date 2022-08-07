using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ThriveLauncher.ViewModels;
using ThriveLauncher.Views;

namespace ThriveLauncher
{
    public class App : Application
    {
        public override void Initialize()
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            // https://github.com/AvaloniaUI/Avalonia/issues/8640
            // Animation.RegisterAnimator<TransformAnimator>(prop => typeof(ITransform).IsAssignableFrom(prop.PropertyType));

            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
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
}
