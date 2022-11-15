namespace ThriveLauncher.Views;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Models.ParsedContent;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Models;
using ReactiveUI;
using Services.Localization;
using SharedBase.Utilities;
using Utilities;
using ViewModels;

public partial class MainWindow : Window
{
    /// <summary>
    ///   Priority to use for the game log output view update tasks. For now this is set lower as this seems like a
    ///   good enough idea as there'll be a ton of log message update tasks.
    /// </summary>
    private const DispatcherPriority LogViewUpdatePriority = DispatcherPriority.Background;

    private readonly List<ComboBoxItem> languageItems = new();
    private readonly List<(IPlayableVersion Version, ComboBoxItem Item)> versionItems = new();

    private readonly Dictionary<FilePrepareProgress, ProgressDisplayer> activeProgressDisplayers = new();

    private readonly object lockForBulkOutputRemove = new();
    private int bulkOutputRemoveCount;

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

    protected override void OnClosed(EventArgs e)
    {
        if (dataContextReceived)
        {
            var dataContext = DerivedDataContext;

            dataContext.ShutdownListeners();

            // Unregister all callbacks in case some of them try to trigger after the close
            dataContext.PlayMessages.CollectionChanged -= OnPlayMessagesChanged;
            dataContext.InProgressPlayOperations.CollectionChanged -= OnPlayPopupProgressChanged;
            dataContext.ThriveOutputFirstPart.CollectionChanged -= OnFirstPartOfOutputChanged;
            dataContext.ThriveOutputLastPart.CollectionChanged -= OnLastPartOfOutputChanged;
        }

        base.OnClosed(e);
    }

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

        dataContext.WhenAnyValue(d => d.LatestAvailableDevBuilds).Subscribe(OnAvailableDevBuildsChanged);

        dataContext.WhenAnyValue(d => d.ThriveIsRunning).Subscribe(OnThriveRunningChanged);
        dataContext.WhenAnyValue(d => d.ShowCloseButtonOnPlayPopup).Subscribe(OnShowCloseButtonOnPlayPopupChanged);

        dataContext.WhenAnyValue(d => d.WantsWindowHidden).Subscribe(OnWantedWindowHiddenStateChanged);
        dataContext.WhenAnyValue(d => d.LauncherShouldClose).Subscribe(OnLauncherWantsToClose);

        dataContext.PlayMessages.CollectionChanged += OnPlayMessagesChanged;

        dataContext.InProgressPlayOperations.CollectionChanged += OnPlayPopupProgressChanged;

        dataContext.ThriveOutputFirstPart.CollectionChanged += OnFirstPartOfOutputChanged;
        dataContext.ThriveOutputLastPart.CollectionChanged += OnLastPartOfOutputChanged;

        // Intentionally left hanging around in the background
        _ = UpdateFeedItemsWhenRetrieved();
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

        // If we don't set the parent window then the license window can be open even after the main window is closed
        // window.Show();

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

    private void OnAvailableDevBuildsChanged(List<DevBuildLauncherDTO> builds)
    {
        LatestBuildsList.Children.Clear();

        if (builds.Count < 1)
            return;

        var botdBinding = new Binding($"[{nameof(Properties.Resources.BuildOfTheDayAbbreviation)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        var unsafeBinding = new Binding($"[{nameof(Properties.Resources.UnsafeBuild)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        var descriptionLabelBinding = new Binding($"[{nameof(Properties.Resources.BuildDescriptionLabelShort)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        var selectBinding = new Binding($"[{nameof(Properties.Resources.SelectButton)}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };

        foreach (var build in builds)
        {
            var container = new WrapPanel();

            var visitButton = new Button
            {
                Classes = new Classes("TextLink"),
                Content = build.BuildHash,
            };

            visitButton.Click += (_, _) => DerivedDataContext.VisitBuildPage(build.Id);

            container.Children.Add(visitButton);

            container.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"({build.Id}, {build.Branch})",
                Margin = new Thickness(0, 0, 5, 0),
            });

            if (build.Anonymous && !build.Verified)
            {
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = unsafeBinding,
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }

            if (build.BuildOfTheDay)
            {
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = botdBinding,
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }

            if (!string.IsNullOrWhiteSpace(build.Description))
            {
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = descriptionLabelBinding,
                    Margin = new Thickness(0, 0, 2, 0),
                });
                container.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = build.Description.Truncate(100),
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }

            var deleteButton = new Button
            {
                [!ContentProperty] = selectBinding,
            };

            deleteButton.Click += (_, _) => DerivedDataContext.SelectManualBuild(build.BuildHash);

            container.Children.Add(deleteButton);

            LatestBuildsList.Children.Add(container);
        }
    }

    private void OnPlayMessagesChanged(object? o, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        // TODO: check that there isn't possibility of the ToList call here crashing due to list changes happening
        // at the same time
        Dispatcher.UIThread.Post(() => HandlePlayMessagesChanged(DerivedDataContext.PlayMessages.ToList()));
    }

    private void HandlePlayMessagesChanged(List<string> playMessages)
    {
        PlayPrepareMessageContainer.Children.Clear();

        if (playMessages.Count < 1)
            return;

        foreach (var message in playMessages)
        {
            PlayPrepareMessageContainer.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = message,
                Margin = new Thickness(0, 0, 0, 0),
            });
        }

        // This is here to ensure when there's a really long devbuild description that the end of the messages is
        // visible
        PlayOutputScrollContainer.ScrollToEnd();
    }

    private void OnPlayPopupProgressChanged(object? o,
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        Dispatcher.UIThread.Post(() => HandlePlayProgressChanges(DerivedDataContext.InProgressPlayOperations.ToList()));
    }

    private void HandlePlayProgressChanges(List<FilePrepareProgress> progress)
    {
        if (progress.Count < 1)
        {
            PlayPrepareProgressContainer.Children.Clear();

            foreach (var displayer in activeProgressDisplayers)
            {
                displayer.Value.Dispose();
            }

            activeProgressDisplayers.Clear();

            return;
        }

        foreach (var displayer in activeProgressDisplayers)
        {
            displayer.Value.Marked = false;
        }

        // We try to preserve as much state as possible here to keep progress bars working fine
        foreach (var progressItem in progress)
        {
            if (!activeProgressDisplayers.TryGetValue(progressItem, out var displayer))
            {
                // No display yet for this progress item
                displayer = new ProgressDisplayer(PlayPrepareProgressContainer, progressItem);
                activeProgressDisplayers[progressItem] = displayer;
            }
            else
            {
                displayer.Marked = true;

                // We don't update here as when we first see a progress item we start listening for its updates
                // separately
            }
        }

        // Delete displayers for progress items no longer present
        foreach (var toDelete in activeProgressDisplayers.Where(t => !t.Value.Marked).ToList())
        {
            toDelete.Value.Dispose();
            activeProgressDisplayers.Remove(toDelete.Key);
        }

        PlayOutputScrollContainer.ScrollToEnd();
    }

    private void OnFirstPartOfOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => HandleFirstPartOfOutputChanged(e), LogViewUpdatePriority);
    }

    private void HandleFirstPartOfOutputChanged(NotifyCollectionChangedEventArgs e)
    {
        IBrush? errorBrush = null;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:

                var children = FirstGameOutputContainer.Children;
                int index = e.NewStartingIndex;

                foreach (var newItem in e.NewItems ?? throw new Exception("New items expected"))
                {
                    if (newItem == null)
                        continue;

                    var message = (ThriveOutputMessage)newItem;

                    if (message.IsError && errorBrush == null)
                    {
                        errorBrush = (IBrush?)Application.Current?.Resources["GameErrorOutput"] ??
                            throw new Exception("Unable to get error brush");
                    }

                    var textBlock = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = message.Message,
                        Margin = new Thickness(0, 0, 0, 0),
                    };

                    if (message.IsError)
                        textBlock.Foreground = errorBrush;

                    children.Insert(index++, textBlock);
                }

                // TODO: somehow skip scrolling if user is holding the scrollbar or scrolled to a position that
                // isn't the end
                PlayOutputScrollContainer.ScrollToEnd();

                break;
            case NotifyCollectionChangedAction.Remove:
                // This shouldn't be ever triggered, but this is included for completeness
                FirstGameOutputContainer.Children.RemoveRange(e.OldStartingIndex, e.OldItems?.Count ?? 1);
                break;
            case NotifyCollectionChangedAction.Reset:
                FirstGameOutputContainer.Children.Clear();
                break;
        }
    }

    private void OnLastPartOfOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // We specially handle remove here to bunch things up as that's *way* better performance
        if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            if (e.OldStartingIndex != 0)
            {
                throw new ArgumentException(
                    "Elements are only expected to be removed from the start for performance reasons");
            }

            lock (lockForBulkOutputRemove)
            {
                int removeCount = e.OldItems?.Count ?? 1;

                if (bulkOutputRemoveCount <= 0)
                {
                    // We are starting a new operation
                    bulkOutputRemoveCount = removeCount;

                    Dispatcher.UIThread.Post(PerformBulkOutputRemove, LogViewUpdatePriority);
                }
                else
                {
                    // We are appending to an existing operation
                    bulkOutputRemoveCount += removeCount;
                }
            }

            return;
        }

        Dispatcher.UIThread.Post(() => HandleLastPartOfOutputChanged(e), LogViewUpdatePriority);
    }

    private void HandleLastPartOfOutputChanged(NotifyCollectionChangedEventArgs e)
    {
        IBrush? errorBrush = null;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:

                var children = LastGameOutputContainer.Children;
                int index = e.NewStartingIndex;

                foreach (var newItem in e.NewItems ?? throw new Exception("New items expected"))
                {
                    if (newItem == null)
                        continue;

                    var message = (ThriveOutputMessage)newItem;

                    if (message.IsError && errorBrush == null)
                    {
                        errorBrush = (IBrush?)Application.Current?.Resources["GameErrorOutput"] ??
                            throw new Exception("Unable to get error brush");
                    }

                    var textBlock = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = message.Message,
                        Margin = new Thickness(0, 0, 0, 0),
                    };

                    if (message.IsError)
                        textBlock.Foreground = errorBrush;

                    children.Insert(index++, textBlock);
                }

                // TODO: see the TODO in HandleFirstPartOfOutputChanged
                PlayOutputScrollContainer.ScrollToEnd();

                break;
            case NotifyCollectionChangedAction.Remove:
                throw new InvalidOperationException("Our caller should have specially handled the remove action");

            case NotifyCollectionChangedAction.Reset:
                LastGameOutputContainer.Children.Clear();
                break;
        }
    }

    private async void PerformBulkOutputRemove()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        int removeCount;
        lock (lockForBulkOutputRemove)
        {
            removeCount = bulkOutputRemoveCount;
            bulkOutputRemoveCount = 0;
        }

        LastGameOutputContainer.Children.RemoveRange(0, removeCount);
    }

    private void OnThriveRunningChanged(bool running)
    {
        if (!running)
        {
            // We need to rebuild the late part of the game output here as there's no other way to properly detect
            // this
            Dispatcher.UIThread.Post(() =>
            {
                var listContent = DerivedDataContext.ThriveOutputLastPart.ToList();

                HandleLastPartOfOutputChanged(
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                HandleLastPartOfOutputChanged(
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, listContent, 0));
            }, DispatcherPriority.Background);

            ScrollOutputToEndWithDelay();
        }
    }

    private void OnShowCloseButtonOnPlayPopupChanged(bool show)
    {
        if (show)
        {
            // If we didn't manage to play Thrive we don't get the not running value change so the last line of the
            // failure to start a build may not be visible if we don't do this here as well
            ScrollOutputToEndWithDelay();
        }
    }

    /// <summary>
    ///   Scrolls the game output to the end after a delay to ensure that *really* the last message is visible
    /// </summary>
    private async void ScrollOutputToEndWithDelay()
    {
        await Task.Delay(TimeSpan.FromSeconds(1));

        Dispatcher.UIThread.Post(() => { PlayOutputScrollContainer.ScrollToEnd(); });
    }

    private void OnWantedWindowHiddenStateChanged(bool hidden)
    {
        // ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162
        if (LauncherConstants.EntirelyHideWindowOnHide)
        {
            if (hidden == !IsVisible)
                return;

            if (hidden)
            {
                Hide();
            }
            else
            {
                Show();
            }

            return;
        }

        var wantedState = hidden ? WindowState.Minimized : WindowState.Normal;

        if (WindowState == wantedState)
            return;

        WindowState = wantedState;

        // Bug workaround: https://github.com/AvaloniaUI/Avalonia/issues/9280
        if (wantedState != WindowState.Minimized)
            Activate();

        // ReSharper restore HeuristicUnreachableCode
#pragma warning restore CS0162
    }

    private void OnLauncherWantsToClose(bool close)
    {
        if (close)
            Close();
    }

    private async void CopyLogsToClipboard(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var text = DerivedDataContext.GetFullOutputForClipboard();

        var clipboard = Application.Current?.Clipboard;

        if (clipboard == null)
            throw new InvalidOperationException("Clipboard doesn't exist");

        await clipboard.SetTextAsync(text);
    }
}
