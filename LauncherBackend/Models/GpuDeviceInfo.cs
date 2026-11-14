namespace LauncherBackend.Models;

using System.Globalization;

public enum GpuDeviceType
{
    Other = 0,
    Integrated = 1,
    Discrete = 2,
    Virtual = 3,
    Cpu = 4,
}

/// <summary>
///   A GPU (Vulkan physical device) detected on the system
/// </summary>
public record GpuDeviceInfo(int Index, string Name, uint VendorId, uint DeviceId, GpuDeviceType Type)
{
    /// <summary>
    ///   Persistent identifier for this device. Used to save the user's GPU selection.
    /// </summary>
    public string Identifier => MakeIdentifier(VendorId, DeviceId);

    public static string MakeIdentifier(uint vendorId, uint deviceId)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:x4}:{1:x4}", vendorId, deviceId);
    }
}
