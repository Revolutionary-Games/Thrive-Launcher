namespace ThriveLauncher.Models;

using System.Globalization;
using LauncherBackend.Models;
using Properties;

/// <summary>
///   An entry in the GPU selection dropdown
/// </summary>
public record GpuSelectionOption(string? Identifier, string? Name, string DisplayText)
{
    public static GpuSelectionOption Automatic => new(null, null, Resources.GpuSelectionAutomatic);

    /// <summary>
    ///   Shown instead of <see cref="Automatic"/> when Vulkan is not available, in which case Thrive will use OpenGL
    /// </summary>
    public static GpuSelectionOption OpenGlFallback => new(null, null, Resources.GpuSelectionOpenGlFallback);

    public static GpuSelectionOption FromDevice(GpuDeviceInfo device)
    {
        return new GpuSelectionOption(device.Identifier, device.Name,
            string.Format(CultureInfo.CurrentCulture, Resources.GpuSelectionDeviceFormat, device.Name,
                GetTypeName(device.Type)));
    }

    public static GpuSelectionOption FromSavedDevice(string identifier, string? name)
    {
        return new GpuSelectionOption(identifier, name, name ?? identifier);
    }

    public static GpuSelectionOption FromMissingDevice(string identifier, string? name)
    {
        return new GpuSelectionOption(identifier, name,
            string.Format(CultureInfo.CurrentCulture, Resources.GpuSelectionNotDetected, name ?? identifier));
    }

    public override string ToString()
    {
        return DisplayText;
    }

    private static string GetTypeName(GpuDeviceType type)
    {
        return type switch
        {
            GpuDeviceType.Discrete => Resources.GpuTypeDiscrete,
            GpuDeviceType.Integrated => Resources.GpuTypeIntegrated,
            GpuDeviceType.Virtual => Resources.GpuTypeVirtual,
            GpuDeviceType.Cpu => Resources.GpuTypeCpu,
            _ => Resources.GpuTypeOther,
        };
    }
}
