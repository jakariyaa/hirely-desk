using System.Text.Encodings.Web;
using AwesomeAssertions;
using CvPlatform.Application.Integration;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using CvPlatform.Web.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CvPlatform.Tests;

public class PositionApiTokenAuthenticationHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly string _token = PositionApiTokenSecret.Create();

    public PositionApiTokenAuthenticationHandlerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseSqlite(_connection)
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var relationalFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using (var db = relationalFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Positions.Add(new Position { Id = _positionId, Title = "Senior .NET Developer", IsPublic = true });
            db.PositionApiTokens.Add(new PositionApiToken
            {
                Id = Guid.NewGuid(),
                PositionId = _positionId,
                Name = "Odoo",
                TokenHash = PositionApiTokenSecret.Hash(_token),
                CreatedAt = DateTime.UtcNow,
            });
            db.PositionApiTokens.Add(new PositionApiToken
            {
                Id = Guid.NewGuid(),
                PositionId = _positionId,
                Name = "Revoked",
                TokenHash = PositionApiTokenSecret.Hash("cvp_revoked"),
                CreatedAt = DateTime.UtcNow,
                RevokedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        _factory = new TestFactory(relationalFactory);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private sealed class StubMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private async Task<(AuthenticateResult Result, DefaultHttpContext Context)>
        AuthenticateAsync(string? authorizationHeader)
    {
        var handler = new PositionApiTokenAuthenticationHandler(
            new StubMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _factory);
        var context = new DefaultHttpContext();
        if (authorizationHeader is not null)
            context.Request.Headers.Authorization = authorizationHeader;
        var scheme = new AuthenticationScheme(
            PositionApiTokenDefaults.Scheme,
            PositionApiTokenDefaults.Scheme,
            typeof(PositionApiTokenAuthenticationHandler));
        await ((IAuthenticationHandler)handler).InitializeAsync(scheme, context);
        return (await handler.AuthenticateAsync(), context);
    }

    [Fact]
    public async Task A_valid_token_authenticates_with_token_and_position_claims()
    {
        var (result, _) = await AuthenticateAsync($"Bearer {_token}");

        result.Succeeded.Should().BeTrue();
        var identity = result.Principal!.Identities.Single();
        identity.AuthenticationType.Should().Be(PositionApiTokenDefaults.Scheme);
        identity.FindFirst(PositionApiTokenDefaults.PositionIdClaim)!.Value.Should().Be(_positionId.ToString());
        identity.FindFirst(PositionApiTokenDefaults.TokenIdClaim).Should().NotBeNull();
    }

    [Fact]
    public async Task An_unknown_token_fails_authentication()
    {
        var (result, _) = await AuthenticateAsync($"Bearer {PositionApiTokenSecret.Create()}");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task A_revoked_token_fails_authentication()
    {
        var (result, _) = await AuthenticateAsync("Bearer cvp_revoked");

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    public async Task Missing_or_non_bearer_credentials_do_not_authenticate(string? header)
    {
        var (result, _) = await AuthenticateAsync(header);

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task The_challenge_advertises_the_bearer_scheme_and_leaves_the_body_to_problem_details()
    {
        var (_, context) = await AuthenticateAsync(null);
        var handler = new PositionApiTokenAuthenticationHandler(
            new StubMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _factory);
        var scheme = new AuthenticationScheme(
            PositionApiTokenDefaults.Scheme,
            PositionApiTokenDefaults.Scheme,
            typeof(PositionApiTokenAuthenticationHandler));
        await ((IAuthenticationHandler)handler).InitializeAsync(scheme, context);

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
    }
}
