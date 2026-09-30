using CvPlatform.Application.Common;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Translates a domain <see cref="Error"/> into an RFC 9457 response. Only
/// <see cref="ErrorCodes.ValidationFailed"/> carries a detail, because those messages come from
/// validators and are written for users; every other code stays opaque.
/// </summary>
public static class ProblemResults
{
    public static IResult From(Error error)
    {
        var statusCode = StatusFor(error.Code);
        var detail = error.Code == ErrorCodes.ValidationFailed ? Blank(error.Message) : null;
        return TypedResults.Problem(ProblemFactory.Create(statusCode, error.Code, detail));
    }

    public static IResult FromStatus(int statusCode) =>
        TypedResults.Problem(ProblemFactory.Create(statusCode, ProblemFactory.CodeForStatus(statusCode)));

    public static int StatusFor(string errorCode) => errorCode switch
    {
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string? Blank(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : message.Trim();
}