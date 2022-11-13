namespace LauncherBackend.Utilities;

using System.Text;

public static class TextUtilities
{
    /// <summary>
    ///   Converts line separators from <c>\n</c> to platform specific ones, while avoiding duplicating existing line
    ///   separators.
    /// </summary>
    /// <param name="stringBuilder"></param>
    public static void MakeLineSeparatorsPlatformSpecific(this StringBuilder stringBuilder)
    {
        stringBuilder.Replace("\n", Environment.NewLine);

        // Fix any cases where we replaced a part of a line separator
        stringBuilder.Replace("\r\r\n", "\r\n");
    }
}
