using CvPlatform.Infrastructure.Support;
using Microsoft.Extensions.Options;

namespace CvPlatform.Web.Integration;

public static class DriveWebhookEndpoints
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void MapDriveWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/integrations/drive/webhook", async (
            HttpContext ctx,
            IOptions<GoogleOptions> options,
            CvPlatform.Infrastructure.Support.SupportTicketProcessor processor,
            CancellationToken ct) =>
        {
            var token = ctx.Request.Headers["X-Goog-Channel-Token"].ToString();
            if (options.Value.WebhookConfigured && !string.Equals(token, options.Value.WebhookToken, StringComparison.Ordinal))
                return Results.Unauthorized();

            var state = ctx.Request.Headers["X-Goog-Resource-State"].ToString();
            if (!string.Equals(state, "sync", StringComparison.OrdinalIgnoreCase))
            {
                await Gate.WaitAsync(ct);
                try
                {
                    await processor.ProcessPendingAsync(ct);
                }
                finally
                {
                    Gate.Release();
                }
            }
            return Results.Ok();
        }).AllowAnonymous();
    }
}
