namespace LauncherBackend.Models;

public enum CrashReporterSubmitResult
{
    Success,
    TooManyRequests,
    ServerError,
    BadRequest,
    NetworkError,
    UnknownError,
}
