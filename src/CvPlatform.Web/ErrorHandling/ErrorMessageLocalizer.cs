using CvPlatform.Application.Common;
using CvPlatform.Web.Resources;
using Microsoft.Extensions.Localization;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Turns stable error codes into localized, user-facing text. Developer messages carried by
/// <see cref="Error"/> are never shown unless the app runs in Development.
/// </summary>
public sealed class ErrorMessageLocalizer(
    IStringLocalizer<SharedResource> localizer,
    IHostEnvironment environment)
{
    public string Describe(Error error) => error.Code switch
    {
        ErrorCodes.NotFound => Text("ErrorNotFound"),
        ErrorCodes.Forbidden => Text("ErrorForbidden"),
        ErrorCodes.Unauthorized => Text("ErrorUnauthorized"),
        ErrorCodes.Conflict => Text("ErrorConflict"),
        ErrorCodes.ConcurrencyConflict => Text("ErrorConcurrency"),
        ErrorCodes.RateLimited => Text("ErrorRateLimited"),
        ErrorCodes.ServiceUnavailable => Text("ErrorServiceUnavailable"),
        ErrorCodes.ValidationFailed => Validation(error.Message),
        _ => Unexpected(error.Message)
    };

    public string Describe(Exception exception) => Unexpected(exception.Message);

    /// <summary>Maps a failure code returned by the client upload module to localized text.</summary>
    public string DescribeUploadFailure(string? code) => code switch
    {
        "invalid_type" => Text("ImageUploadTypeInvalid"),
        "too_large" => Text("ImageUploadTooLarge"),
        "ticket_failed" => Text("ImageUploadTicketFailed"),
        "storage_rejected" => Text("ImageUploadRejected"),
        "verify_failed" => Text("ImageUploadVerifyFailed"),
        _ => Text("ImageUploadFailed")
    };

    private string Validation(string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? Text("ErrorValidation")
            : $"{Text("ErrorValidation")} {detail.Trim()}";

    private string Unexpected(string? message) =>
        environment.IsDevelopment() && !string.IsNullOrWhiteSpace(message)
            ? $"{Text("ErrorUnexpected")} ({message.Trim()})"
            : Text("ErrorUnexpected");

    private string Text(string key) => localizer[key].Value;
}