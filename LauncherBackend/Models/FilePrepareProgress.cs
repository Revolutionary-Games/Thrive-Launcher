namespace LauncherBackend.Models;

/// <summary>
///   Describes the progress of a file to be processed for Thrive to be playable.
/// </summary>
public class FilePrepareProgress : IObservable<FilePrepareProgress>
{
    private readonly List<IObserver<FilePrepareProgress>> observers = new();

    private float? currentProgress;
    private float? finishedProgress;

    private string? downloadUrlToShow;
    private string? downloadSource;

    public FilePrepareProgress(string fileIdentifier, FilePrepareStep step)
    {
        FileIdentifier = fileIdentifier;
        CurrentStep = step;
    }

    public FilePrepareProgress(string fileIdentifier, string downloadUrlToShow, string downloadSource) : this(
        fileIdentifier, FilePrepareStep.Downloading)
    {
        DownloadUrlToShow = downloadUrlToShow;
        DownloadSource = downloadSource;
    }

    public string FileIdentifier { get; }

    public float? CurrentProgress
    {
        get => currentProgress;
        set
        {
            // As these are nullable doing an epsilon comparison would be complex here
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (currentProgress == value)
                return;

            currentProgress = value;
            Notify();
        }
    }

    public float? FinishedProgress
    {
        get => finishedProgress;
        set
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (finishedProgress == value)
                return;

            finishedProgress = value;
            Notify();
        }
    }

    public FilePrepareStep CurrentStep { get; private set; }

    public string? DownloadUrlToShow
    {
        get => downloadUrlToShow;
        set
        {
            if (downloadUrlToShow == value)
                return;

            downloadUrlToShow = value;
            Notify();
        }
    }

    public string? DownloadSource
    {
        get => downloadSource;
        set
        {
            if (downloadSource == value)
                return;

            downloadSource = value;
            Notify();
        }
    }

    public bool ProgressIsIndeterminate => CurrentProgress == null || FinishedProgress == null;

    public void MoveToExtractStep()
    {
        if (CurrentStep != FilePrepareStep.Downloading && CurrentStep != FilePrepareStep.Verifying)
            throw new ArgumentException("Can only move to extract step from download or verify steps");

        PerformStepMove(FilePrepareStep.Extracting);
    }

    public IDisposable Subscribe(IObserver<FilePrepareProgress> observer)
    {
        if (!observers.Contains(observer))
            observers.Add(observer);

        return new Subscription(observers, observer);
    }

    private void Notify()
    {
        foreach (var observer in observers)
        {
            observer.OnNext(this);
        }
    }

    private void PerformStepMove(FilePrepareStep newStep)
    {
        CurrentStep = newStep;
        downloadSource = null;
        downloadUrlToShow = null;

        // To help other code we set 1 to be the full progress for the next step (this is not meant to be used when
        // moving to the download step). But current progress is null so the end result is that the progress is
        // indeterminate initially.
        finishedProgress = 1;
        currentProgress = null;

        Notify();
    }

    private class Subscription : IDisposable
    {
        private readonly List<IObserver<FilePrepareProgress>> observers;
        private readonly IObserver<FilePrepareProgress> observer;

        public Subscription(List<IObserver<FilePrepareProgress>> observers, IObserver<FilePrepareProgress> observer)
        {
            this.observers = observers;
            this.observer = observer;
        }

        public void Dispose()
        {
            observers.Remove(observer);
        }
    }
}
