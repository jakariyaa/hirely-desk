using System.Security.Claims;
using System.Text.Encodings.Web;
using CvPlatform.Application.Integration;
using CvPlatform.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CvPlatform.Web.Integration;

public static class PositionApiTokenDefaults
{
    public const string Scheme = "PositionApiToken";
    public const string Policy = "PositionApiToken";
    public const string TokenIdClaim = "position_api_token_id";
    public const string PositionIdClaim = "position_api_token_position_id";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Reads the bearer credential from the <c>Authorization</c> header. Shared by the
    /// authentication handler and the rate limiter so both interpret the header identically.
    /// </summary>
    public static bool TryReadBearer(HttpRequest request, out string token)
    {
        token = "";
        if (!request.Headers.TryGetValue("Authorization", out var header))
            return false;

        var value = header.ToString();
        if (!value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = value[BearerPrefix.Length..].Trim();
        return token.Length > 0;
    }
}

/// <summary>
/// Authenticates external API requests that carry a per-position token in
/// <c>Authorization: Bearer cvp_…</c>. The presented secret is hashed and matched against an
/// active (not revoked) token row; the raw token is never logged or persisted.
/// </summary>
public sealed class PositionApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAppDbContextFactory factory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PositionApiTokenDefaults.TryReadBearer(Request, out var token))
            return AuthenticateResult.NoResult();

        var hash = PositionApiTokenSecret.Hash(token);
        await using var db = factory.CreateDbContext();
        var match = await db.PositionApiTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .Select(t => new { t.Id, t.PositionId })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (match is null)
            return AuthenticateResult.Fail("The API token is invalid or revoked.");

        var identity = new ClaimsIdentity(
        [
            new Claim(PositionApiTokenDefaults.TokenIdClaim, match.Id.ToString()),
            new Claim(PositionApiTokenDefaults.PositionIdClaim, match.PositionId.ToString()),
        ], PositionApiTokenDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, PositionApiTokenDefaults.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Leave the body to the status code pages middleware so API clients get RFC 9457
        // problem details while the header still advertises the bearer scheme.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
