namespace CvPlatform.Web.ErrorHandling;

/// <summary>Decides whether a response should be machine readable or rendered as an HTML page.</summary>
public static class ResponseNegotiation
{
    private const string ApiPrefix = "/api";

    public static bool IsApiRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool ExpectsProblemDetails(HttpContext context)
    {
        if (IsApiRequest(context))
            return true;

        var accept = context.Request.Headers.Accept;
        return accept.Count == 0 || !accept.Any(HeaderAcceptsHtml);
    }

    private static bool HeaderAcceptsHtml(string? accept) =>
        accept is not null && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}