namespace LauncherBackend.Models;

/// <summary>
///   Messages from the installation or Thrive running services
/// </summary>
public class ThrivePlayMessage
{
    /// <summary>
    ///   The type of the message. This is done this way to allow translations to be handled by whoever displays
    ///   the message.
    /// </summary>
    public enum Type
    {
        Downloading,
        DownloadingFailed,
    }

    public ThrivePlayMessage(Type type, params object?[] arguments)
    {
        MessageType = type;
        FormatArguments = arguments;
    }

    public object?[] FormatArguments { get; }
    public Type MessageType { get; }

    public string Format(string formatString)
    {
        return string.Format(formatString, FormatArguments);
    }
}
