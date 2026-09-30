using CvPlatform.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Builds RFC 9457 problem details payloads. <see cref="ErrorCodes"/> are the stable contract that
/// clients localize, so <c>title</c> stays a fixed summary and never carries exception details.
/// </summary>
public static class ProblemFactory
{
    public const string ErrorCodeKey = "errorCode";

    public static ProblemDetails Create(int statusCode, string errorCode, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = TitleFor(errorCode, statusCode),
            Detail = detail,
            Type = $"urn:cvplatform:error:{errorCode}"
        };
        problem.Extensions[ErrorCodeKey] = errorCode;
        return problem;
    }

        /// <summary>Maps a bare HTTP status code onto the closest stable error code.</summary>
    public static string CodeForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
        _ when statusCode >= 500 => ErrorCodes.Unexpected,
        _ => ErrorCodes.ValidationFailed
    };

    private static string TitleFor(string errorCode, int statusCode) => errorCode switch
    {
        ErrorCodes.NotFound => "The requested resource was not found.",
        ErrorCodes.Forbidden => "You do not have access to this resource.",
        ErrorCodes.Unauthorized => "Authentication is required.",
        ErrorCodes.ValidationFailed => "The request is not valid.",
        ErrorCodes.Conflict => "The request conflicts with the current data.",
        ErrorCodes.ConcurrencyConflict => "The resource was modified by someone else.",
        ErrorCodes.RateLimited => "Too many requests. Please try again later.",
        ErrorCodes.ServiceUnavailable => "The service is temporarily unavailable.",
        _ => $"Request failed with status {statusCode}."
    };
}