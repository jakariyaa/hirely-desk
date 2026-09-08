using System.Security.Claims;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Security;
using Microsoft.AspNetCore.Identity;

namespace CvPlatform.Web.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/Account/ExternalLogin", (
            string provider,
            string? returnUrl,
            HttpContext ctx,
            LinkGenerator links,
            SignInManager<ApplicationUser> signIn) =>
        {
            var callback = links.GetPathByName(ctx, "ExternalLoginCallback",
                values: new { returnUrl }) ?? $"/Account/ExternalLoginCallback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
            var properties = signIn.ConfigureExternalAuthenticationProperties(provider, callback);
            return Results.Challenge(properties, [provider]);
        }).RequireRateLimiting("auth");

        app.MapGet("/Account/ExternalLoginCallback", async (
            string? returnUrl,
            string? remoteError,
            HttpContext ctx,
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users,
            IAppDbContextFactory factory) =>
        {
            returnUrl ??= "/";
            if (remoteError is not null)
                return Results.Redirect($"/Account/Login?error={Uri.EscapeDataString(remoteError)}");

            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.Redirect("/Account/Login");

            var signInResult = await signIn.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (signInResult.Succeeded)
                return Results.Redirect(RedirectUrlHelper.SafeReturnUrl(returnUrl));

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Results.Redirect("/Account/Login?error=noemail");

            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                await users.AddLoginAsync(existing, info);
                await signIn.SignInAsync(existing, isPersistent: false);
                return Results.Redirect(RedirectUrlHelper.SafeReturnUrl(returnUrl));
            }

            var user = new ApplicationUser { UserName = email, Email = email };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return Results.Redirect("/Account/Login?error=createfailed");

            await users.AddLoginAsync(user, info);
            using var db = factory.CreateDbContext();
            db.Profiles.Add(new Profile { Id = Guid.NewGuid(), UserId = user.Id });
            await db.SaveChangesAsync(ctx.RequestAborted);
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Redirect(RedirectUrlHelper.SafeReturnUrl(returnUrl));
        }).WithName("ExternalLoginCallback").RequireRateLimiting("auth");
    }
}
