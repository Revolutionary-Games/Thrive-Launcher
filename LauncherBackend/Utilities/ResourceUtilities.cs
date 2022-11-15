namespace LauncherBackend.Utilities;

using System.Reflection;
using System.Text;

public static class ResourceUtilities
{
    public static async Task<string> ReadManifestResourceAsync(string name, Assembly? assembly = null)
    {
        var buffer = await ReadManifestResourceRawAsync(name, assembly);

        return Encoding.UTF8.GetString(buffer, 0, buffer.Length);
    }

    public static async Task<byte[]> ReadManifestResourceRawAsync(string name, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        await using var reader = assembly.GetManifestResourceStream(name);

        if (reader == null)
            throw new FileNotFoundException($"Missing manifest resource '{name}'");

        var length = reader.Length;

        if (length < 1)
            return Array.Empty<byte>();

        var buffer = new byte[length];

        var read = await reader.ReadAsync(buffer, 0, (int)length);

        if (read != length)
            throw new IOException("Read number of bytes from manifest resource doesn't match its size");

        return buffer;
    }
}
