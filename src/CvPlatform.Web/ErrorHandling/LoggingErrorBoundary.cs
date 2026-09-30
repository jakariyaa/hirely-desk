using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Last-resort boundary for interactive components. Without it an unhandled exception is fatal to
/// the whole Blazor circuit, so the user loses the page and has to reload.
/// </summary>
public class LoggingErrorBoundary : ErrorBoundary
{
    [Inject]
    private ILogger<LoggingErrorBoundary> Logger { get; set; } = default!;

    /// <summary>Trace id of the failed operation, shown to the user so support can find the log.</summary>
    public string ReferenceId { get; private set; } = string.Empty;

    protected override Task OnErrorAsync(Exception exception)
    {
        ReferenceId = Activity.Current?.TraceId.ToString()
            ?? ActivityTraceId.CreateRandom().ToString();
        Logger.LogError(
            exception,
            "Unhandled exception caught by the global error boundary. {ReferenceId}",
            ReferenceId);
        return Task.CompletedTask;
    }
}