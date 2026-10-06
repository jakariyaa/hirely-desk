namespace CvPlatform.Application.Common;

/// <summary>Stable error codes shared across services and mapped to localized UI messages.</summary>
public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string ValidationFailed = "validation_failed";
    public const string Conflict = "conflict";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string Unauthorized = "unauthorized";
    public const string RateLimited = "rate_limited";
    public const string ServiceUnavailable = "service_unavailable";
    public const string Unexpected = "unexpected";

    /// <summary>Every code the platform can emit, in the order used by tests and diagnostics.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        NotFound,
        Forbidden,
        ValidationFailed,
        Conflict,
        ConcurrencyConflict,
        Unauthorized,
        RateLimited,
        ServiceUnavailable,
        Unexpected
    ];
}
