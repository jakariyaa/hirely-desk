using CvPlatform.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Single global handler for unhandled exceptions. Logs once, then answers with problem details for
/// machine clients and lets the configured exception handler page render for browser requests.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request aborted by the client. {TraceId} {Path}",
                ErrorMapping.TraceIdOf(httpContext),
                httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            await httpContext.Response.CompleteAsync();
            return true;
        }

        var mapping = ErrorMapping.Of(exception);
        var traceId = ErrorMapping.TraceIdOf(httpContext);

        if (mapping.IsServerFault)
        {
            logger.LogError(
                exception,
                "Unhandled exception. {ErrorCode} {TraceId} {Method} {Path}",
                mapping.Code,
                traceId,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Request rejected. {ErrorCode} {TraceId} {Method} {Path}",
                mapping.Code,
                traceId,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        if (IsBlazorSignalRRequest(httpContext))
        {
            httpContext.Response.StatusCode = mapping.StatusCode;
            await httpContext.Response.CompleteAsync();
            return true;
        }

        if (!ResponseNegotiation.ExpectsProblemDetails(httpContext))
            return false;

        if (!httpContext.Response.HasStarted)
            httpContext.Response.Clear();

        httpContext.Response.StatusCode = mapping.StatusCode;
        var detail = environment.IsDevelopment() ? exception.Message : null;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = ProblemFactory.Create(mapping.StatusCode, mapping.Code, detail)
        });

        return true;
    }

    private static bool IsBlazorSignalRRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase);
}