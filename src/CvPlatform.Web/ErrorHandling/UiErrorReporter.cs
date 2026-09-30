using CvPlatform.Application.Common;
using MudBlazor;

namespace CvPlatform.Web.ErrorHandling;

public sealed class UiErrorReporter(
    ISnackbar snackbar,
    ErrorMessageLocalizer messages,
    ILogger<UiErrorReporter> logger) : IUiErrorReporter
{
    public string Report(Error error)
    {
        if (error.Code == ErrorCodes.ValidationFailed)
            logger.LogInformation("Rejected by validation. {Message}", error.Message);
        else
            logger.LogWarning("Operation failed. {ErrorCode} {Message}", error.Code, error.Message);

        return messages.Describe(error);
    }

    public string Report(Exception exception)
    {
        logger.LogError(exception, "Unhandled exception during a user operation.");
        return messages.Describe(exception);
    }

    public void Notify(Error error) => Show(Report(error));

    public void Notify(Exception exception) => Show(Report(exception));

    private void Show(string message) =>
        snackbar.Add(message, Severity.Error, static options =>
        {
            options.RequireInteraction = true;
            options.ShowCloseIcon = true;
        });
}