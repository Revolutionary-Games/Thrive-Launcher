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
        /// <summary>
        ///   Download is happening, is given one parameter with the download URL
        /// </summary>
        Downloading,

        /// <summary>
        ///   Download failed, is given one parameter with the error
        /// </summary>
        DownloadingFailed,

        /// <summary>
        ///   Extraction failed, is given one parameter with the error
        /// </summary>
        ExtractionFailed,

        /// <summary>
        ///   Running failed due to missing folder, is given one parameter with the problematic folder
        /// </summary>
        MissingThriveFolder,

        /// <summary>
        ///   Running failed due to missing Thrive executable, is given one parameter with the problematic folder
        /// </summary>
        MissingThriveExecutable,

        /// <summary>
        ///   Thrive is starting, no parameters
        /// </summary>
        StartingThrive,

        /// <summary>
        ///   Thrive is configured with extra flags, is given one parameter with the extra flags
        /// </summary>
        ExtraStartFlags,

        /// <summary>
        ///   Rehydration of a folder failed, is given one parameter with the dehydrated info file
        /// </summary>
        RehydrationFailed,

        /// <summary>
        ///   Rehydration is starting, no parameters
        /// </summary>
        Rehydrating,
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
