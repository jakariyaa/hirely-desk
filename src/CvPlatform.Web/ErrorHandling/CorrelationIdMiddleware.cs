using System.Diagnostics;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>Publishes the current trace id so operators can join user reports to server logs.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = traceId;
            return Task.CompletedTask;
        });
        await next(context);
    }
}