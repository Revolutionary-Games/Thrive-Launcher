namespace ThriveLauncher.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LauncherBackend.Models;
using LauncherBackend.Models.ParsedContent;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Services.Localization;
using SharedBase.Utilities;
using Utilities;
using ViewModels;

public partial class MainWindow : Window
{
    private readonly List<ComboBoxItem> languageItems = new();
    private readonly List<(IPlayableVersion Version, ComboBoxItem Item)> versionItems = new();

    private bool dataContextReceived;
    private IThriveInstaller? installer;

    private bool usChangingSelectedVersion;

    public MainWindow()
    {
        InitializeComponent();

        DataContextProperty.Changed.Subscribe(OnDataContextReceiver);
    }

    private MainWindowViewModel DerivedDataContext =>
        (MainWindowViewModel?)DataContext ?? throw new Exception("DataContext not initialized");

    /// <summary>
    ///   Don't worry we don't mess with the logic of this, just use some sorting helpers from here
    /// </summary>
    private IThriveInstaller ThriveInstaller => installer ?? throw new Exception("DataContext not initialized");

    private void OnDataContextReceiver(AvaloniaPropertyChangedEventArgs e)
    {
        if (dataContextReceived || e.NewValue == null)
            return;

        // Prevents recursive calls
        dataContextReceived = true;

        var dataContext = DerivedDataContext;

        languageItems.AddRange(dataContext.GetAvailableLanguages().Select(l => new ComboBoxItem { Content = l }));

        LanguageComboBox.Items = languageItems;

        installer = this.GetServiceProvider().GetRequiredService<IThriveInstaller>();

        dataContext.WhenAnyValue(d => d.SelectedLauncherLanguage).Subscribe(OnLanguageChanged);

        dataContext.WhenAnyValue(d => d.SelectedVersionToPlay).Subscribe(OnSelectedVersionChanged);
        dataContext.WhenAnyValue(d => d.AvailableThriveVersions).Subscribe(OnAvailableVersionsChanged);

        dataContext.WhenAnyValue(d => d.InstalledFolders).Subscribe(OnInstalledVersionListChanged);
        dataContext.WhenAnyValue(d => d.TemporaryFolderFiles).Subscribe(OnTemporaryFolderFilesChanged);

        // Intentionally left hanging around in the background
#pragma warning disable CS4014
        UpdateFeedItemsWhenRetrieved();
#pragma warning restore CS4014
    }

    private void SelectedVersionComboBoxItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (usChangingSelectedVersion)
            return;

        _ = sender;

        // Ignore while initializing
        if (DataContext == null)
            return;

        string? selected = null;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] != null)
        {
            selected = (string)((ComboBoxItem)e.AddedItems[0]!).Content;
        }

        DerivedDataContext.VersionSelected(selected);
    }

    private void OnSelectedVersionChanged(string? selectedVersion)
    {
        if (selectedVersion == null)
        {
            VersionComboBox.SelectedItem = null;
            return;
        }

        var selected = versionItems.First(i => (string)i.Item.Content == selectedVersion);

        usChangingSelectedVersion = true;
        VersionComboBox.SelectedItem = selected.Item;
        usChangingSelectedVersion = false;
    }

    private void OnAvailableVersionsChanged(IEnumerable<(string VersionName, IPlayableVersion VersionObject)>? versions)
    {
        versionItems.Clear();

        if (versions != null)
        {
            foreach (var version in ThriveInstaller.SortVersions(versions))
            {
                versionItems.Add((version.VersionObject, new ComboBoxItem
                {
                    // This has the more accurate user readable name
                    Content = version.VersionObject.VersionName,
                }));
            }
        }

        VersionComboBox.Items = versionItems.Select(i => i.Item);
    }

    private void LanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;

        if (e.AddedItems.Count != 1 || e.AddedItems[0] == null)
            throw new ArgumentException("Expected one item to be selected");

        var selected = (ComboBoxItem)e.AddedItems[0]!;

        DerivedDataContext.SelectedLauncherLanguage = (string)selected.Content;
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

    private async void SelectNewThriveInstallLocation(object? sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;

        var dialog = new OpenFolderDialog
        {
            Directory = DerivedDataContext.ThriveInstallationPath,
        };

        var result = await dialog.ShowAsync(this);

        if (string.IsNullOrWhiteSpace(result))
            return;

        DerivedDataContext.SetInstallPathTo(result);
    }

    private async void SelectNewTemporaryLocation(object? sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;

        var dialog = new OpenFolderDialog
        {
            Directory = DerivedDataContext.TemporaryDownloadsFolder,
        };

        var result = await dialog.ShowAsync(this);

        if (string.IsNullOrWhiteSpace(result))
            return;

        DerivedDataContext.SetTemporaryLocationTo(result);
    }

    private async void SelectDevBuildCacheLocation(object? sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;

        var dialog = new OpenFolderDialog
        {
            Directory = DerivedDataContext.DehydratedCacheFolder,
        };

        var result = await dialog.ShowAsync(this);

        if (string.IsNullOrWhiteSpace(result))
            return;

        DerivedDataContext.SetDehydrateCachePathTo(result);
    }

    private async Task UpdateFeedItemsWhenRetrieved()
    {
        var devForum = await DerivedDataContext.DevForumFeedItems;
        var mainSite = await DerivedDataContext.MainSiteFeedItems;

        Dispatcher.UIThread.Post(() =>
        {
            PopulateFeed(this.FindControl<StackPanel>("DevelopmentFeedItems"), devForum);
            PopulateFeed(this.FindControl<StackPanel>("MainSiteFeedItems"), mainSite);
        });
    }

    private void PopulateFeed(IPanel targetContainer, List<ParsedLauncherFeedItem> items)
    {
        foreach (var child in targetContainer.Children.ToList())
        {
            targetContainer.Children.Remove(child);
        }

        var linkClasses = new Classes("TextLink");

        var lightGrey = new SolidColorBrush((Color?)Application.Current?.Resources["LightGrey"] ??
            throw new Exception("missing brush"));

        // TODO: these items need to be recreated if language changes (or bindings need to be used)
        foreach (var feedItem in items)
        {
            var itemContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };

            var title = new Button
            {
                Classes = linkClasses,
                Content = new TextBlock
                {
                    Text = feedItem.Title,
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            title.Click += (_, _) => URLUtilities.OpenURLInBrowser(feedItem.Link);

            itemContainer.Children.Add(title);

            var authorAndTime = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,

                // TODO: a library for x hours ago
                Text = string.Format(Properties.Resources.FeedItemPostedByAndTime,
                    LauncherFeeds.GetPosterUsernameToDisplay(feedItem),
                    RecentTimeString.FormatRecentTimeInLocalTime(feedItem.PublishedAt, false)),
                Margin = new Thickness(20, 0, 0, 8),
                FontSize = 12,
                Foreground = lightGrey,
            };

            itemContainer.Children.Add(authorAndTime);

            var summaryContainer = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
            };

            itemContainer.Children.Add(summaryContainer);

            Control? lastContentItem = null;

            // Main content
            // TODO: this is text and link only for now
            foreach (var parsedFeedContent in feedItem.ParsedSummary)
            {
                if (parsedFeedContent is Text text)
                {
                    lastContentItem = new TextBlock
                    {
                        Text = text.Content,
                        Margin = lastContentItem is TextBlock ? new Thickness(0, 0, 0, 5) : new Thickness(0),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    summaryContainer.Children.Add(lastContentItem);
                }
                else if (parsedFeedContent is Link link)
                {
                    var linkButton = new Button
                    {
                        Classes = linkClasses,
                        Content = new TextBlock
                        {
                            Text = link.Text,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    };

                    linkButton.Click += (_, _) => URLUtilities.OpenURLInBrowser(link.Target);

                    lastContentItem = linkButton;
                    summaryContainer.Children.Add(lastContentItem);
                }
            }

            var bottomMargin = new Thickness(0, 0, 0, 15);

            if (feedItem.Truncated)
            {
                var truncatedContainer = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                };

                var truncatedLink = new Button
                {
                    Classes = linkClasses,
                    Content = Properties.Resources.ClickHereLink,
                    Margin = bottomMargin,
                };

                truncatedLink.Click += (_, _) => URLUtilities.OpenURLInBrowser(feedItem.Link);

                truncatedContainer.Children.Add(truncatedLink);
                truncatedContainer.Children.Add(new TextBlock
                {
                    Text = Properties.Resources.TruncatedClickSuffix,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = bottomMargin,
                });

                itemContainer.Children.Add(truncatedContainer);
            }
            else
            {
                if (lastContentItem != null)
                {
                    lastContentItem.Margin = bottomMargin;
                }
            }

            targetContainer.Children.Add(itemContainer);
        }
    }

    private void OnInstalledVersionListChanged(IEnumerable<FolderInInstallFolder>? installed)
    {
        InstalledFoldersList.Children.Clear();

        if (installed == null)
            return;

        // See LocalizeExtension
        var deleteBinding = new Binding($"[{nameof(Properties.Resources.Delete)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        var unknownBinding = new Binding($"[{nameof(Properties.Resources.UnknownFolder)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        foreach (var installFolder in installed)
        {
            var container = new WrapPanel();

            if (!installFolder.IsThriveFolder)
            {
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = installFolder.FolderName,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = unknownBinding,
                    Margin = new Thickness(0, 0, 3, 0),
                });
            }

            container.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = installFolder.FolderName,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, !installFolder.IsThriveFolder ? 3 : 0, 0),
            });

            if (installFolder.IsThriveFolder)
            {
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,

                    // TODO: make this react to language change somehow
                    Text = string.Format(Properties.Resources.InstalledFolderSize,
                        string.Format(Properties.Resources.SizeInMiB,
                            Math.Round((float)installFolder.Size / GlobalConstants.MEBIBYTE, 2))),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0),
                });

                var deleteButton = new Button
                {
                    Classes = new Classes("Danger"),
                    [!ContentProperty] = deleteBinding,
                };

                deleteButton.Click += (_, _) => DerivedDataContext.DeleteVersion(installFolder.FolderName);

                container.Children.Add(deleteButton);
            }

            InstalledFoldersList.Children.Add(container);
        }

        // Add some blank space at the bottom
        InstalledFoldersList.Children.Add(new TextBlock { Text = " " });
    }

    private void OnTemporaryFolderFilesChanged(IEnumerable<string>? files)
    {
        bool empty = true;

        TemporaryFilesList.Children.Clear();

        if (files != null)
        {
            foreach (var file in files)
            {
                empty = false;

                TemporaryFilesList.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Path.GetFileName(file),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        if (empty)
        {
            TemporaryFilesList.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                [!TextBlock.TextProperty] = new Binding($"[{nameof(Properties.Resources.FolderIsEmpty)}]")
                {
                    Mode = BindingMode.OneWay,
                    Source = Localizer.Instance,
                },
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }
}
