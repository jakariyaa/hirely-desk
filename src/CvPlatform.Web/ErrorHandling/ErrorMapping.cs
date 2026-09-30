using System.Diagnostics;
using CvPlatform.Application.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>A single place that decides how an exception becomes an HTTP status and a stable error code.</summary>
public readonly record struct ErrorMapping(int StatusCode, string Code, bool IsServerFault)
{
    private const string UniqueViolation = "23505";

    public static ErrorMapping Of(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException =>
            new(StatusCodes.Status409Conflict, ErrorCodes.ConcurrencyConflict, false),
        DbUpdateException dbException when IsUniqueViolation(dbException) =>
            new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, false),
        KeyNotFoundException =>
            new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, false),
        UnauthorizedAccessException =>
            new(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, false),
        AntiforgeryValidationException =>
            new(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, false),
        BadHttpRequestException badRequest =>
            new(badRequest.StatusCode, ErrorCodes.ValidationFailed, false),
        TimeoutException =>
            new(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceUnavailable, true),
        HttpRequestException =>
            new(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceUnavailable, true),
        FormatException =>
            new(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, false),
        _ => new(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected, true)
    };

    public static string TraceIdOf(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}