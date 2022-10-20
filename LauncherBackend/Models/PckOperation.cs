namespace LauncherBackend.Models;

using System.Text.Json.Serialization;

public class PckOperation
{
    public PckOperation(string filePath, string targetNameInPck)
    {
        FilePath = filePath;
        TargetNameInPck = targetNameInPck;
    }

    [JsonPropertyName("file")]
    public string FilePath { get; set; }

    [JsonPropertyName("target")]
    public string TargetNameInPck { get; set; }
}
