using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace CvPlatform.Web.ErrorHandling;

/// <summary>
/// Renders every bodyless 400-599 response: problem details for machine clients, and the matching
/// HTML page for browsers. Registered with <c>UseStatusCodePages</c>.
/// </summary>
public static class StatusCodePageWriter
{
    private const string ErrorPagePath = "/Error";
    private const string NotFoundPagePath = "/not-found";

    public static async Task WriteAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;
        var statusCode = httpContext.Response.StatusCode;
        if (statusCode < StatusCodes.Status400BadRequest)
            return;

        if (ResponseNegotiation.ExpectsProblemDetails(httpContext))
        {
            await WriteProblemAsync(httpContext, statusCode);
            return;
        }

        var target = statusCode >= StatusCodes.Status500InternalServerError
            ? ErrorPagePath
            : NotFoundPagePath;

        if (!HttpMethods.IsGet(httpContext.Request.Method) ||
            PathMatches(httpContext.Request.Path, target))
        {
            await WritePlainTextAsync(httpContext, statusCode);
            return;
        }

        var originalPath = httpContext.Request.Path;
        var originalQueryString = httpContext.Request.QueryString;
        var reExecuteFeature = httpContext.Features.Get<IStatusCodeReExecuteFeature>();
        try
        {
            httpContext.Features.Set<IStatusCodeReExecuteFeature>(
                new ReExecuteFeature(
                    originalPath,
                    httpContext.Request.PathBase,
                    originalQueryString,
                    target));
            httpContext.Request.Path = target;
            httpContext.Request.QueryString = QueryString.Empty;
            await context.Next(httpContext);
        }
        finally
        {
            httpContext.Request.Path = originalPath;
            httpContext.Request.QueryString = originalQueryString;
            httpContext.Features.Set(reExecuteFeature);
        }

        if (!httpContext.Response.HasStarted)
            httpContext.Response.StatusCode = statusCode;
    }

    private static async Task WriteProblemAsync(HttpContext httpContext, int statusCode)
    {
        if (httpContext.Response.ContentLength.HasValue || httpContext.Response.HasStarted)
            return;

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        httpContext.Response.ContentType = "application/problem+json";
        httpContext.Response.StatusCode = statusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = ProblemFactory.Create(statusCode, ProblemFactory.CodeForStatus(statusCode))
        });
    }

    private static Task WritePlainTextAsync(HttpContext httpContext, int statusCode)
    {
        httpContext.Response.ContentType = "text/plain; charset=utf-8";
        var title = ProblemFactory.Create(statusCode, ProblemFactory.CodeForStatus(statusCode)).Title;
        return httpContext.Response.WriteAsync($"{statusCode} {title}");
    }

    private static bool PathMatches(PathString path, string target) =>
        string.Equals(path.Value?.TrimEnd('/'), target, StringComparison.OrdinalIgnoreCase);

    private sealed class ReExecuteFeature(
        PathString originalPath,
        PathString originalPathBase,
        QueryString originalQueryString,
        PathString currentPath) : IStatusCodeReExecuteFeature
    {
        public string OriginalPath { get; set; } = originalPath.Value ?? string.Empty;

        public string OriginalPathBase { get; set; } = originalPathBase.Value ?? string.Empty;

        public string? OriginalQueryString { get; set; } = originalQueryString.Value;

        public PathString CurrentPath { get; } = currentPath;

        public bool ReExecute { get; set; }
    }
}
