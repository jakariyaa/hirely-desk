using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Web.Auth;

namespace CvPlatform.Web.Storage;

public static class ProfileImageEndpoints
{
    public static void MapProfileImageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/profile-images/{id:guid}/content", async (
            Guid id,
            Guid? cvId,
            HttpContext context,
            IProfileImageService images,
            CancellationToken cancellationToken) =>
        {
            var actor = ActorContexts.TryFromUser(context.User);
            if (actor is null)
                return Results.Unauthorized();

            var result = await images.CreateDownloadTicketAsync(
                actor, id, cvId, cancellationToken);
            if (!result.Succeeded)
                return result.Error.Code switch
                {
                    ErrorCodes.NotFound => Results.NotFound(),
                    ErrorCodes.Forbidden => Results.Forbid(),
                    _ => Results.BadRequest(result.Error.Message),
                };

            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Redirect(result.Value!.DownloadUrl);
        })
        .RequireAuthorization()
        .RequireRateLimiting("image-downloads");
    }
}
