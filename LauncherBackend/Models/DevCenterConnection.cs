namespace LauncherBackend.Models;

/// <summary>
///   A valid (last time we checked from the client) connection to the devcenter
/// </summary>
public class DevCenterConnection
{
    public DevCenterConnection(string username, bool isDeveloper)
    {
        Username = username;
        IsDeveloper = isDeveloper;
    }

    public string Username { get; set; }
    public bool IsDeveloper { get; set; }
}
