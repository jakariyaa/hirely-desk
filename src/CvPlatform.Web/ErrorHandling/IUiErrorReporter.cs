using CvPlatform.Application.Common;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// The single client-side exit for failures. Every failing operation goes through here so the user
/// always gets localized text and the underlying cause is always logged.
/// </summary>
public interface IUiErrorReporter
{
    /// <summary>Logs the failure and returns localized text for inline display.</summary>
    string Report(Error error);

    /// <summary>Logs the exception and returns localized text for inline display.</summary>
    string Report(Exception exception);

    /// <summary>Logs the failure and shows it in a snackbar.</summary>
    void Notify(Error error);

    /// <summary>Logs the exception and shows it in a snackbar.</summary>
    void Notify(Exception exception);
}