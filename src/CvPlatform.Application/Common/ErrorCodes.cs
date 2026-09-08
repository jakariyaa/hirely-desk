namespace CvPlatform.Application.Common;

/// <summary>Stable error codes shared across services and mapped to localized UI messages.</summary>
public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string ValidationFailed = "validation_failed";
    public const string Conflict = "conflict";
    public const string ConcurrencyConflict = "concurrency_conflict";
}
