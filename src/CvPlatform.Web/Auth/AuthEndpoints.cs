using System.Security.Claims;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Security;
using CvPlatform.Application.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/Account/Logout", async (
            HttpContext ctx,
            SignInManager<ApplicationUser> signIn,
            IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(ctx);
            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect("/");
        })
        .RequireAuthorization()
        .RequireRateLimiting("auth");

        app.MapGet("/Account/ExternalLogin", async (
            string provider,
            string? returnUrl,
            HttpContext ctx,
            LinkGenerator links,
            SignInManager<ApplicationUser> signIn,
            IAuthenticationSchemeProvider schemes) =>
        {
            var scheme = await schemes.GetSchemeAsync(provider);
            if (scheme is null || string.IsNullOrWhiteSpace(scheme.DisplayName))
                return Results.Redirect("/Account/Login?error=providerunavailable");

            var safeReturnUrl = RedirectUrlHelper.SafeReturnUrl(returnUrl);
            var callback = links.GetPathByName(ctx, "ExternalLoginCallback",
                values: new { returnUrl = safeReturnUrl })
                ?? $"/Account/ExternalLoginCallback?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
            var properties = signIn.ConfigureExternalAuthenticationProperties(provider, callback);
            return Results.Challenge(properties, [provider]);
        }).AllowAnonymous().RequireRateLimiting("auth");

        app.MapGet("/Account/ExternalLoginCallback", async (
            string? returnUrl,
            string? remoteError,
            HttpContext ctx,
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users,
            IAppDbContextFactory factory,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ExternalLogin");
            returnUrl ??= "/";
            if (remoteError is not null)
            {
                logger.LogWarning("External provider returned an authentication error for {ReturnUrl}.",
                    RedirectUrlHelper.SafeReturnUrl(returnUrl));
                return Results.Redirect($"/Account/Login?error=providererror&returnUrl={Uri.EscapeDataString(RedirectUrlHelper.SafeReturnUrl(returnUrl))}");
            }

            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
            {
                logger.LogWarning("External login callback did not contain login information.");
                return Results.Redirect($"/Account/Login?error=providererror&returnUrl={Uri.EscapeDataString(RedirectUrlHelper.SafeReturnUrl(returnUrl))}");
            }

            var signInResult = await signIn.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
            if (signInResult.Succeeded)
                return Results.Redirect(RedirectUrlHelper.SafeReturnUrl(returnUrl));

            if (signInResult.IsLockedOut)
                return Results.Redirect("/Account/Login?error=lockedout");
            if (signInResult.RequiresTwoFactor)
                return Results.Redirect("/Account/Login?error=twofactor");
            if (signInResult.IsNotAllowed)
                return Results.Redirect("/Account/Login?error=notallowed");

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Results.Redirect("/Account/Login?error=noemail");

            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
                return Results.Redirect("/Account/Login?error=externallinkrequired");

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return Results.Redirect("/Account/Login?error=createfailed");

            var roleResult = await users.AddToRoleAsync(user, Roles.Candidate);
            if (!roleResult.Succeeded)
            {
                await users.DeleteAsync(user);
                return Results.Redirect("/Account/Login?error=createfailed");
            }

            var loginResult = await users.AddLoginAsync(user, info);
            if (!loginResult.Succeeded)
            {
                await users.DeleteAsync(user);
                return Results.Redirect("/Account/Login?error=createfailed");
            }

            try
            {
                await using var db = factory.CreateDbContext();
                var profileExists = await db.Profiles.AnyAsync(p => p.UserId == user.Id, ctx.RequestAborted);
                if (!profileExists)
                {
                    db.Profiles.Add(new Profile { Id = Guid.NewGuid(), UserId = user.Id });
                    await db.SaveChangesAsync(ctx.RequestAborted);
                }
            }
            catch (DbUpdateException exception)
            {
                logger.LogError(
                    exception, "Could not create profile for external user {UserId}.", user.Id);
                await users.DeleteAsync(user);
                return Results.Redirect("/Account/Login?error=createfailed");
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Redirect(RedirectUrlHelper.SafeReturnUrl(returnUrl));
        }).WithName("ExternalLoginCallback").AllowAnonymous().RequireRateLimiting("auth");

        app.MapGet("/Account/LinkExternalLogin", async (
            string provider,
            string? returnUrl,
            HttpContext ctx,
            LinkGenerator links,
            SignInManager<ApplicationUser> signIn,
            IAuthenticationSchemeProvider schemes) =>
        {
            var scheme = await schemes.GetSchemeAsync(provider);
            if (scheme is null || string.IsNullOrWhiteSpace(scheme.DisplayName))
                return Results.Redirect("/profile?error=providerunavailable");

            var safeReturnUrl = RedirectUrlHelper.SafeReturnUrl(returnUrl ?? "/profile");
            var callback = links.GetPathByName(ctx, "LinkExternalLoginCallback",
                values: new { returnUrl = safeReturnUrl })
                ?? $"/Account/LinkExternalLoginCallback?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
            var properties = signIn.ConfigureExternalAuthenticationProperties(provider, callback);
            return Results.Challenge(properties, [provider]);
        }).RequireAuthorization().RequireRateLimiting("auth");

        app.MapGet("/Account/LinkExternalLoginCallback", async (
            string? returnUrl,
            string? remoteError,
            HttpContext ctx,
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users) =>
        {
            var safeReturnUrl = RedirectUrlHelper.SafeReturnUrl(returnUrl ?? "/profile");
            if (remoteError is not null)
                return Results.Redirect($"/profile?error=providererror");

            var info = await signIn.GetExternalLoginInfoAsync();
            var current = await users.GetUserAsync(ctx.User);
            if (info is null || current is null)
                return Results.Redirect("/profile?error=providererror");

            var linkedUser = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linkedUser is not null && linkedUser.Id != current.Id)
                return Results.Redirect("/profile?error=provideralreadylinked");

            var result = linkedUser is null
                ? await users.AddLoginAsync(current, info)
                : IdentityResult.Success;
            if (!result.Succeeded)
                return Results.Redirect("/profile?error=linkfailed");

            await signIn.RefreshSignInAsync(current);
            return Results.Redirect(safeReturnUrl);
        }).WithName("LinkExternalLoginCallback").RequireAuthorization().RequireRateLimiting("auth");
    }
}
