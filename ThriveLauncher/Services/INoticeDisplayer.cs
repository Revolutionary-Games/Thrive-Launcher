namespace ThriveLauncher.Services;

public interface INoticeDisplayer
{
    public void ShowNotice(string title, string text, bool canDismiss = true);
}
