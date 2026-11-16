namespace LauncherBackend.Services;

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Lists the GPUs available through Vulkan.
/// </summary>
public interface IGpuDeviceEnumerator
{
    /// <summary>
    ///   True when GPU selection is supported on the current platform
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    ///   Enumerates the GPUs. Creates a Vulkan instance each time so this is somewhat slow (should be called from a
    ///   background thread when possible).
    /// </summary>
    /// <returns>The list of devices, empty if Vulkan is not available or failed</returns>
    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices();
}

public class GpuDeviceEnumerator : IGpuDeviceEnumerator
{
    private const int VkSuccess = 0;
    private const int VkIncomplete = 5;

    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;

    // VK_API_VERSION_1_0
    private const uint VkApiVersion10 = 1U << 22;

    private const int VkMaxPhysicalDeviceNameSize = 256;

    // Offsets of the fields we read from VkPhysicalDeviceProperties.
    private const int PropertiesVendorIdOffset = 8;
    private const int PropertiesDeviceIdOffset = 12;
    private const int PropertiesDeviceTypeOffset = 16;
    private const int PropertiesDeviceNameOffset = 20;

    // The real struct is 824 bytes, this has some extra room just in case
    private const int PropertiesBufferSize = 4096;

    private readonly ILogger<GpuDeviceEnumerator> logger;
    private readonly Lock enumerationLock = new();

    public GpuDeviceEnumerator(ILogger<GpuDeviceEnumerator> logger)
    {
        this.logger = logger;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateInstance(ref VkInstanceCreateInfo createInfo, nint allocator,
        out nint instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstance(nint instance, nint allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumeratePhysicalDevices(nint instance, ref uint deviceCount, nint devices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceProperties(nint physicalDevice, nint properties);

    /// <summary>
    ///   Thrive uses Metal on macOS so GPU selection through Vulkan device indices doesn't apply there
    /// </summary>
    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!IsSupported)
            return [];

        // Avoid multiple threads loading and using Vulkan at the same time
        lock (enumerationLock)
        {
            try
            {
                return EnumerateDevicesInternal();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to enumerate GPUs through Vulkan");
                return [];
            }
        }
    }

    private static string GetVulkanLibraryName()
    {
        if (OperatingSystem.IsWindows())
            return "vulkan-1.dll";

        return "libvulkan.so.1";
    }

    private static T GetFunction<T>(nint library, string name)
        where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }

    private IReadOnlyList<GpuDeviceInfo> EnumerateDevicesInternal()
    {
        if (!NativeLibrary.TryLoad(GetVulkanLibraryName(), out var library))
        {
            logger.LogInformation("Vulkan library is not available, cannot list GPUs");
            return [];
        }

        // Note that we intentionally don't free the library here. Vulkan drivers don't always like being unloaded
        // and loaded again, and the OS reference counts the library anyway so loading it again is cheap.
        var createInstance = GetFunction<VkCreateInstance>(library, "vkCreateInstance");
        var destroyInstance = GetFunction<VkDestroyInstance>(library, "vkDestroyInstance");
        var enumeratePhysicalDevices =
            GetFunction<VkEnumeratePhysicalDevices>(library, "vkEnumeratePhysicalDevices");
        var getPhysicalDeviceProperties =
            GetFunction<VkGetPhysicalDeviceProperties>(library, "vkGetPhysicalDeviceProperties");

        var applicationName = Marshal.StringToCoTaskMemUTF8("Thrive Launcher");
        var applicationInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<VkApplicationInfo>());
        var properties = Marshal.AllocCoTaskMem(PropertiesBufferSize);
        var instance = nint.Zero;

        try
        {
            Marshal.StructureToPtr(new VkApplicationInfo
            {
                SType = VkStructureTypeApplicationInfo,
                ApplicationName = applicationName,
                EngineName = applicationName,
                ApiVersion = VkApiVersion10,
            }, applicationInfo, false);

            var createInfo = new VkInstanceCreateInfo
            {
                SType = VkStructureTypeInstanceCreateInfo,
                ApplicationInfo = applicationInfo,
            };

            int result = createInstance(ref createInfo, nint.Zero, out instance);
            if (result != VkSuccess)
            {
                logger.LogWarning("Failed to create Vulkan instance for listing GPUs, result: {Result}", result);
                instance = nint.Zero;
                return [];
            }

            uint count = 0;
            result = enumeratePhysicalDevices(instance, ref count, nint.Zero);
            if (result != VkSuccess || count == 0)
            {
                logger.LogWarning("Failed to get Vulkan device count (result: {Result}, count: {Count})", result,
                    count);
                return [];
            }

            var deviceHandles = new nint[count];
            var handleBuffer = Marshal.AllocCoTaskMem(nint.Size * (int)count);
            try
            {
                result = enumeratePhysicalDevices(instance, ref count, handleBuffer);

                // Incomplete is fine, it just means a device disappeared between the calls
                if (result != VkSuccess && result != VkIncomplete)
                {
                    logger.LogWarning("Failed to enumerate Vulkan devices, result: {Result}", result);
                    return [];
                }

                Marshal.Copy(handleBuffer, deviceHandles, 0, (int)count);
            }
            finally
            {
                Marshal.FreeCoTaskMem(handleBuffer);
            }

            var devices = new List<GpuDeviceInfo>((int)count);

            for (int i = 0; i < count; ++i)
            {
                // Clear the buffer so that the device name is always null terminated
                Marshal.Copy(new byte[PropertiesBufferSize], 0, properties, PropertiesBufferSize);

                getPhysicalDeviceProperties(deviceHandles[i], properties);

                var vendorId = (uint)Marshal.ReadInt32(properties, PropertiesVendorIdOffset);
                var deviceId = (uint)Marshal.ReadInt32(properties, PropertiesDeviceIdOffset);
                var type = Marshal.ReadInt32(properties, PropertiesDeviceTypeOffset);
                var name = Marshal.PtrToStringUTF8(properties + PropertiesDeviceNameOffset) ?? string.Empty;

                if (name.Length > VkMaxPhysicalDeviceNameSize)
                    name = name.Substring(0, VkMaxPhysicalDeviceNameSize);

                var deviceType = Enum.IsDefined(typeof(GpuDeviceType), type) ?
                    (GpuDeviceType)type :
                    GpuDeviceType.Other;

                devices.Add(new GpuDeviceInfo(i, name, vendorId, deviceId, deviceType));
            }

            logger.LogDebug("Detected GPUs: {Devices}", string.Join(", ", devices));

            return devices;
        }
        finally
        {
            if (instance != nint.Zero)
                destroyInstance(instance, nint.Zero);

            Marshal.FreeCoTaskMem(properties);
            Marshal.FreeCoTaskMem(applicationInfo);
            Marshal.FreeCoTaskMem(applicationName);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkApplicationInfo
    {
        public int SType;
        public nint Next;
        public nint ApplicationName;
        public uint ApplicationVersion;
        public nint EngineName;
        public uint EngineVersion;
        public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkInstanceCreateInfo
    {
        public int SType;
        public nint Next;
        public uint Flags;
        public nint ApplicationInfo;
        public uint EnabledLayerCount;
        public nint EnabledLayerNames;
        public uint EnabledExtensionCount;
        public nint EnabledExtensionNames;
    }
}
