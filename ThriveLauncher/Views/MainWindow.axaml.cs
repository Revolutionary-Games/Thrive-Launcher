using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using ThriveLauncher.Utilities;
using ThriveLauncher.ViewModels;

namespace ThriveLauncher.Views
{
    public partial class MainWindow : Window
    {
        private readonly List<ComboBoxItem> languageItems = new();
        private bool dataContextReceived;

        public MainWindow()
        {
            InitializeComponent();

            DataContextProperty.Changed.Subscribe(OnDataContextReceiver);
        }

        private void OnDataContextReceiver(AvaloniaPropertyChangedEventArgs e)
        {
            if (dataContextReceived || e.NewValue == null)
                return;

            // Prevents recursive calls
            dataContextReceived = true;

            var dataContext = (MainWindowViewModel)e.NewValue;

            languageItems.AddRange(dataContext.GetAvailableLanguages().Select(l => new ComboBoxItem { Content = l }));

            LanguageComboBox.Items = languageItems;

            dataContext.WhenAnyValue(d => d.SelectedLauncherLanguage).Subscribe(OnLanguageChanged);
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

        private void LanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _ = sender;

            if (e.AddedItems.Count != 1 || e.AddedItems[0] == null)
                throw new ArgumentException("Expected one item to be selected");

            var selected = (ComboBoxItem)e.AddedItems[0]!;

            ((MainWindowViewModel)DataContext!).SelectedLauncherLanguage = (string)selected.Content;
        }

        private void OnLanguageChanged(string selectedLanguage)
        {
            var selected = languageItems.First(i => (string)i.Content == selectedLanguage);

            LanguageComboBox.SelectedItem = selected;
        }

        private void OpenLicensesWindow(object? sender, RoutedEventArgs routedEventArgs)
        {
            _ = sender;
            _ = routedEventArgs;

            var window = new LicensesWindow
            {
                DataContext = this.CreateInstance<LicensesWindowViewModel>(),
            };
            window.Show(this);
        }
    }
}
