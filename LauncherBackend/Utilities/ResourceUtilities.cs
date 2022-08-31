namespace LauncherBackend.Utilities;

using System.Reflection;
using System.Text;

public static class ResourceUtilities
{
    public static async Task<string> ReadManifestResourceAsync(string name, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        await using var reader = assembly.GetManifestResourceStream(name);

        if (reader == null)
            throw new FileNotFoundException($"Missing manifest resource '{name}'");

        var length = reader.Length;

        if (length < 1)
            return string.Empty;

        var buffer = new byte[length];

        var read = await reader.ReadAsync(buffer, 0, (int)length);

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
