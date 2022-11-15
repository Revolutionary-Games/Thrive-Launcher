namespace LauncherBackend.Services;

/// <summary>
///   Thrown when all verifying keys are expired
/// </summary>
public class AllKeysExpiredException : Exception
{
    public AllKeysExpiredException(Exception innerException) : base(null, innerException)
    {
    }
}
