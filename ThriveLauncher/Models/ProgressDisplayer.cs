namespace ThriveLauncher.Models;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LauncherBackend.Models;
using Properties;
using SharedBase.Utilities;

/// <summary>
///   Handles showing data from <see cref="FilePrepareProgress"/> in various Controls to show user ongoing progress
/// </summary>
public class ProgressDisplayer : IDisposable, IObserver<FilePrepareProgress>
{
    private Panel? parent;
    private StackPanel? container;
    private IDisposable? listener;

    private TextBlock? label;
    private ProgressBar? progressBar;
    private TextBlock? textualProgress;

    public ProgressDisplayer(Panel parent, FilePrepareProgress initialProgress)
    {
        this.parent = parent;

        container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(5, 10),
        };

        label = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        UpdateLabel(initialProgress);
        container.Children.Add(label);

        progressBar = new ProgressBar
        {
            Minimum = 0,
        };
        UpdateProgressBar(initialProgress);
        container.Children.Add(progressBar);

        textualProgress = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        UpdateTextualProgress(initialProgress);
        container.Children.Add(textualProgress);

        parent.Children.Add(container);

        // For each FilePrepareProgress object we need to start listening to its updates as updating the single
        // items don't cause a whole list update notice
        listener = initialProgress.Subscribe(this);
    }

    public bool Marked { get; set; } = true;

    public void Update(FilePrepareProgress progress)
    {
        if (container == null)
            return;

        UpdateProgressBar(progress);
        UpdateLabel(progress);
        UpdateTextualProgress(progress);
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(FilePrepareProgress value)
    {
        Dispatcher.UIThread.Post(() => { Update(value); }, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (listener != null)
            {
                listener.Dispose();
                listener = null;
            }

            if (parent != null)
            {
                if (container != null)
                    parent.Children.Remove(container);

                parent = null;
            }

            container = null;
            label = null;
            progressBar = null;
            textualProgress = null;
        }
    }

    private void UpdateLabel(FilePrepareProgress progress)
    {
        switch (progress.CurrentStep)
        {
            case FilePrepareStep.Downloading:
                label!.Text = string.Format(Resources.DownloadingStateItemProgress,
                    progress.FileIdentifier, progress.DownloadUrlToShow);
                break;
            case FilePrepareStep.Verifying:
                label!.Text = string.Format(Resources.VerifyingStateItemProgress,
                    progress.FileIdentifier);
                break;
            case FilePrepareStep.Extracting:
                label!.Text = string.Format(Resources.ExtractingStateItemProgress,
                    progress.FileIdentifier);
                break;
            case FilePrepareStep.Processing:
                label!.Text = string.Format(Resources.ProcessingStateItemProgress,
                    progress.FileIdentifier);
                break;
            default:
                label!.Text = $"Error: unhandled progress step: {progress.CurrentStep}";
                break;
        }
    }

    private void UpdateProgressBar(FilePrepareProgress progress)
    {
        if (progress.ProgressIsIndeterminate)
        {
            progressBar!.IsIndeterminate = true;
        }
        else
        {
            progressBar!.IsIndeterminate = false;
            progressBar.Maximum = progress.FinishedProgress ?? 0;
            progressBar.Value = progress.CurrentProgress ?? 0;
        }
    }

    private void UpdateTextualProgress(FilePrepareProgress progress)
    {
        if (progress.CurrentStep == FilePrepareStep.Downloading && progress.CurrentProgress != null)
        {
            textualProgress!.IsVisible = true;

            var maxValue = ((double?)progress.FinishedProgress)?.BytesToMiB(2, false) ?? Resources.UnknownNumber;

            textualProgress!.Text = string.Format(Resources.DownloadProgressDisplay,
                ((double)progress.CurrentProgress.Value).BytesToMiB(2, false, true), maxValue);
        }
        else if (progress.CurrentStep == FilePrepareStep.Processing && progress.CurrentProgress != null)
        {
            textualProgress!.IsVisible = true;

            var maxValue = progress.FinishedProgress?.ToString() ?? Resources.UnknownNumber;

            textualProgress!.Text = string.Format(Resources.ItemProgressDisplay,
                progress.CurrentProgress.ToString(), maxValue);
        }
        else
        {
            textualProgress!.IsVisible = false;
        }
    }
}
