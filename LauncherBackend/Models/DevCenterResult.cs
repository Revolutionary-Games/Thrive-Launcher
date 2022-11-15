namespace LauncherBackend.Models;

public enum DevCenterResult
{
    /// <summary>
    ///   Operation succeeded
    /// </summary>
    Success,

    /// <summary>
    ///   We can't connect to the DevCenter
    /// </summary>
    ConnectionFailure,

    /// <summary>
    ///   Our key is invalid and we shouldn't use it again (this situation does not fix itself)
    /// </summary>
    InvalidKey,

    /// <summary>
    ///   An unknown error happened that the user can't really correct
    /// </summary>
    DataError,
}
