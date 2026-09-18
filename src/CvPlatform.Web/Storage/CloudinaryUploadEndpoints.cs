using CvPlatform.Infrastructure.Storage;
using CvPlatform.Web.Auth;

namespace CvPlatform.Web.Storage;

public static class CloudinaryUploadEndpoints
{
    public static void MapCloudinaryUploadEndpoints(this WebApplication app)
    {
        app.MapGet("/api/profile-images/signature", (
            HttpContext context,
            ICloudinaryUploadSigner signer) =>
        {
            var actor = ActorContexts.TryFromUser(context.User);
            if (actor is null)
                return Results.Unauthorized();

            context.Response.Headers.CacheControl = "no-store";
            var signature = signer.Create(actor.UserId);
            return signature is null
                ? Results.Problem(
                    "Image upload is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(signature);
        })
        .RequireAuthorization()
        .RequireRateLimiting("uploads");
    }
}
