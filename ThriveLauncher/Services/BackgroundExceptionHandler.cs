namespace ThriveLauncher.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Properties;

/// <summary>
///   Handles showing background operation errors
/// </summary>
public class BackgroundExceptionHandler : IBackgroundExceptionNoticeDisplayer
{
    private readonly ILogger<BackgroundExceptionHandler> logger;
    private readonly List<INoticeDisplayer> noticeDisplayers = new();

    public BackgroundExceptionHandler(ILogger<BackgroundExceptionHandler> logger)
    {
        this.logger = logger;
    }

    public void HandleTask(Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                logger.LogError(t.Exception, "An exception happened in a background task");

                lock (noticeDisplayers)
                {
                    foreach (var noticeDisplayer in noticeDisplayers)
                    {
                        noticeDisplayer.ShowNotice(Resources.InternalErrorTitle, Resources.InternalErrorExplanation);
                    }
                }
            }
        });

        if (task.Status == TaskStatus.Created)
            task.Start();
    }

    public void RegisterErrorDisplayer(INoticeDisplayer noticeDisplayer)
    {
        lock (noticeDisplayers)
        {
            if (noticeDisplayers.Contains(noticeDisplayer))
                throw new ArgumentException("Already registered");

            noticeDisplayers.Add(noticeDisplayer);
        }
    }

    public bool RemoveErrorDisplayer(INoticeDisplayer noticeDisplayer)
    {
        lock (noticeDisplayers)
        {
            return noticeDisplayers.Remove(noticeDisplayer);
        }
    }
}
