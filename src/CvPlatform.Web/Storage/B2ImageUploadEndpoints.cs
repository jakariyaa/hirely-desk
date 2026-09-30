using CvPlatform.Core.Storage;
using CvPlatform.Web.Auth;
using CvPlatform.Web.ErrorHandling;
using Microsoft.AspNetCore.Antiforgery;

namespace CvPlatform.Web.Storage;

public static class B2ImageUploadEndpoints
{
    public static void MapB2ImageUploadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/profile-images/presign", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IImageStorage imageStorage,
            PresignImageUploadRequest? request) =>
        {
            if (!await IsAntiforgeryValidAsync(context, antiforgery))
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            var actor = ActorContexts.TryFromUser(context.User);
            if (actor is null)
                return Results.Unauthorized();
            if (!imageStorage.IsConfigured)
                return ProblemResults.FromStatus(StatusCodes.Status503ServiceUnavailable);
            if (request is null || string.IsNullOrWhiteSpace(request.ContentType) ||
                request.Size <= 0 || request.Size > imageStorage.MaxUploadBytes)
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);
            if (!imageStorage.IsAllowedContentType(request.ContentType))
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            var ticket = imageStorage.CreateUploadTicket(
                actor.UserId,
                request.ContentType,
                request.Size);
            if (ticket is null)
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(ticket);
        })
        .RequireAuthorization()
        .RequireRateLimiting("uploads");

        app.MapPost("/api/profile-images/complete", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IImageStorage imageStorage,
            CompleteImageUploadRequest? request,
            CancellationToken cancellationToken) =>
        {
            if (!await IsAntiforgeryValidAsync(context, antiforgery))
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            var actor = ActorContexts.TryFromUser(context.User);
            if (actor is null)
                return Results.Unauthorized();
            if (request is null || string.IsNullOrWhiteSpace(request.ObjectKey) ||
                string.IsNullOrWhiteSpace(request.ContentType) || request.Size <= 0)
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            var objectKey = await imageStorage.CompleteUploadAsync(
                actor.UserId,
                request.ObjectKey,
                request.ContentType,
                request.Size,
                cancellationToken);
            if (objectKey is null)
                return ProblemResults.FromStatus(StatusCodes.Status400BadRequest);

            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new CompleteImageUploadResponse(objectKey));
        })
        .RequireAuthorization()
        .RequireRateLimiting("uploads");
    }

    private static async Task<bool> IsAntiforgeryValidAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    public sealed record PresignImageUploadRequest(string ContentType, long Size);

    public sealed record CompleteImageUploadRequest(
        string ObjectKey,
        string ContentType,
        long Size);

    public sealed record CompleteImageUploadResponse(string ObjectKey);
}
