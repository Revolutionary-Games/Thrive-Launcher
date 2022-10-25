namespace LauncherBackend.Models;

/// <summary>
///   Single line of output from Thrive
/// </summary>
public class ThriveOutputMessage
{
    public ThriveOutputMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }

    /// <summary>
    ///   True when this was in the error output (and was not sanitized from Steam output)
    /// </summary>
    public bool IsError { get; }

    public override string ToString()
    {
        if (IsError)
        {
            return $"ERROR: {Message}";
        }

        return Message;
    }
}
