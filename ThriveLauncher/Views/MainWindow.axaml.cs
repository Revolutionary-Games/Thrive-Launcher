using System;
using Avalonia.Controls;
using ThriveLauncher.ViewModels;

namespace ThriveLauncher.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectedVersionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _ = sender;

            // Ignore while initializing
            if (DataContext == null)
                return;

            if (e.AddedItems.Count != 1 || e.AddedItems[0] == null)
                throw new ArgumentException("Expected one item to be selected");

            var selected = (ComboBoxItem)e.AddedItems[0]!;

            ((MainWindowViewModel)DataContext).VersionSelected((string)selected.Content);
        }
    }
}
