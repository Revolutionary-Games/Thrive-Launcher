namespace ThriveLauncher.Services;

using LauncherBackend.Services;

public interface IBackgroundExceptionNoticeDisplayer : IBackgroundExceptionHandler
{
    public void RegisterErrorDisplayer(INoticeDisplayer noticeDisplayer);
    public bool RemoveErrorDisplayer(INoticeDisplayer noticeDisplayer);
}
