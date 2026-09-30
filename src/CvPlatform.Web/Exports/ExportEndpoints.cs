using System.Security.Claims;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Exports;
using CvPlatform.Web.Auth;
using CvPlatform.Web.ErrorHandling;

namespace CvPlatform.Web.Exports;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/exports/cvs/{id:guid}/pdf", async (
            Guid id,
            HttpContext ctx,
            IExportService exports,
            CancellationToken ct) =>
        {
            var actor = ActorOf(ctx.User);
            if (actor is null)
                return Results.Unauthorized();
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var result = await exports.ExportCvPdfAsync(actor, id, baseUrl, ct);
            return result.Succeeded
                ? Results.File(result.Value!, "application/pdf", $"cv-{id}.pdf")
                : ProblemResults.From(result.Error);
        }).RequireAuthorization();

        app.MapGet("/api/exports/positions/xlsx", async (
            HttpContext ctx,
            IExportService exports,
            CancellationToken ct) =>
        {
            var actor = ActorOf(ctx.User);
            if (actor is null)
                return Results.Unauthorized();
            var result = await exports.ExportPositionsExcelAsync(actor, ct);
            return result.Succeeded
                ? Results.File(result.Value!,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "positions.xlsx")
                : ProblemResults.From(result.Error);
        }).RequireAuthorization();

        app.MapGet("/api/exports/positions/{positionId:guid}/cvs.xlsx", async (
            Guid positionId,
            HttpContext ctx,
            IExportService exports,
            CancellationToken ct) =>
        {
            var actor = ActorOf(ctx.User);
            if (actor is null)
                return Results.Unauthorized();
            var result = await exports.ExportPositionCvsExcelAsync(actor, positionId, ct);
            return result.Succeeded
                ? Results.File(result.Value!,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"position-{positionId}-cvs.xlsx")
                : ProblemResults.From(result.Error);
        }).RequireAuthorization();

        app.MapGet("/api/exports/positions/{positionId:guid}/cvs.csv", async (
            Guid positionId,
            HttpContext ctx,
            IExportService exports,
            CancellationToken ct) =>
        {
            var actor = ActorOf(ctx.User);
            if (actor is null)
                return Results.Unauthorized();
            var result = await exports.ExportPositionCvsCsvAsync(actor, positionId, ct);
            return result.Succeeded
                ? Results.File(result.Value!, "text/csv; charset=utf-8", $"position-{positionId}-cvs.csv")
                : ProblemResults.From(result.Error);
        }).RequireAuthorization();
    }

    private static ActorContext? ActorOf(ClaimsPrincipal user) => ActorContexts.TryFromUser(user);
}
