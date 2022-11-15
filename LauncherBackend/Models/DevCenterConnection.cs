namespace LauncherBackend.Models;

using DevCenterCommunication.Models;

/// <summary>
///   A valid (last time we checked from the client) connection to the devcenter
/// </summary>
public class DevCenterConnection
{
    public DevCenterConnection(string username, string email, bool isDeveloper)
    {
        Username = username;
        Email = email;
        IsDeveloper = isDeveloper;
    }

    public DevCenterConnection(LauncherConnectionStatus fromStatus)
    {
        if (!fromStatus.Valid)
            throw new ArgumentException("Status needs to be in valid state");

        Username = fromStatus.Username;
        Email = fromStatus.Email;
        IsDeveloper = fromStatus.Developer;
    }

    public string Username { get; set; }
    public string Email { get; set; }
    public bool IsDeveloper { get; set; }
}
