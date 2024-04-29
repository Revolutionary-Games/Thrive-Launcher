namespace ThriveLauncher.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;

/// <summary>
///   Warning window if the user CPU is likely not good enough to run Thrive
/// </summary>
public partial class CPUFeatureWarning : Window
{
    public CPUFeatureWarning()
    {
        InitializeComponent();
    }

    private void ClickClose(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        Close();
    }
}
