using System.Security.Claims;
using CvPlatform.Application.Integration;
using CvPlatform.Web.ErrorHandling;

namespace CvPlatform.Web.Integration;

public static class PositionSummaryEndpoints
{
    public static void MapPositionSummaryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/positions/summary", async (
            HttpContext ctx,
            IPositionSummaryService summaries,
            CancellationToken ct) =>
        {
            var positionId = Guid.TryParse(
                ctx.User.FindFirstValue(PositionApiTokenDefaults.PositionIdClaim), out var id)
                ? id
                : (Guid?)null;
            var tokenId = Guid.TryParse(
                ctx.User.FindFirstValue(PositionApiTokenDefaults.TokenIdClaim), out var token)
                ? token
                : (Guid?)null;
            if (positionId is null)
                return ProblemResults.FromStatus(StatusCodes.Status401Unauthorized);

            ctx.Response.Headers.CacheControl = "no-store";
            var result = await summaries.GetSummaryAsync(positionId.Value, tokenId, ct);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : ProblemResults.From(result.Error);
        })
        .RequireAuthorization(PositionApiTokenDefaults.Policy)
        .RequireRateLimiting("position-api");
    }
}
