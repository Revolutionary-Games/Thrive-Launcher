namespace LauncherBackend.Services;

public interface IBackgroundExceptionHandler
{
    /// <summary>
    ///   Takes care of catching exceptions from a task with <c>ContinueWith</c>
    /// </summary>
    /// <param name="task">The task to add exception handling for</param>
    public void HandleTask(Task task);
}
