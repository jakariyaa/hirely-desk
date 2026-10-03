using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Integration;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionApiTokenServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;
    private readonly PositionApiTokenService _service;
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _recruiterId = Guid.NewGuid();
    private readonly ActorContext _recruiter;

    public PositionApiTokenServiceTests()
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
            db.Users.Add(new ApplicationUser { Id = _recruiterId, UserName = "recruiter" });
            db.Positions.Add(new Position
            {
                Id = _positionId,
                OwnerId = _recruiterId,
                Title = "Senior .NET Developer",
                IsPublic = true,
            });
            db.SaveChanges();
        }

        _factory = new TestFactory(relationalFactory);
        _service = new PositionApiTokenService(_factory);
        _recruiter = new ActorContext(_recruiterId, IsAdmin: false, IsRecruiter: true);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    [Fact]
    public async Task Generated_token_is_prefixed_and_only_the_hash_is_stored()
    {
        var created = await _service.GenerateAsync(_recruiter, _positionId, "Odoo production");

        created.Succeeded.Should().BeTrue();
        var token = created.Value!.Token;
        token.Should().StartWith(PositionApiTokenSecret.Prefix);
        token.Length.Should().BeGreaterThan(40);

        await using var db = _factory.CreateDbContext();
        var entity = await db.PositionApiTokens.SingleAsync();
        entity.TokenHash.Should().Be(PositionApiTokenSecret.Hash(token));
        entity.TokenHash.Should().NotContain(token);
        entity.Name.Should().Be("Odoo production");
    }

    [Fact]
    public async Task Token_name_longer_than_the_column_is_rejected_as_validation()
    {
        var tooLong = new string('x', PositionApiToken.NameMaxLength + 1);

        var result = await _service.GenerateAsync(_recruiter, _positionId, tooLong);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Several_tokens_can_be_active_for_the_same_position()
    {
        var first = await _service.GenerateAsync(_recruiter, _positionId, "Odoo prod");
        var second = await _service.GenerateAsync(_recruiter, _positionId, "Odoo test");
        var third = await _service.GenerateAsync(_recruiter, _positionId, null);

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        third.Succeeded.Should().BeTrue();
        third.Value!.Name.Should().Be("API token");

        var listed = await _service.ListAsync(_recruiter, _positionId);
        listed.Succeeded.Should().BeTrue();
        listed.Value!.Should().HaveCount(3).And.OnlyContain(t => t.IsActive);
        listed.Value.Select(t => t.Name).Should().BeEquivalentTo(["Odoo prod", "Odoo test", "API token"]);
    }

    [Fact]
    public async Task Revoking_one_token_leaves_the_others_active()
    {
        var first = (await _service.GenerateAsync(_recruiter, _positionId, "first")).Value!;
        var second = (await _service.GenerateAsync(_recruiter, _positionId, "second")).Value!;

        var revoked = await _service.RevokeAsync(_recruiter, _positionId, first.Id);

        revoked.Succeeded.Should().BeTrue();
        var listed = (await _service.ListAsync(_recruiter, _positionId)).Value!;
        listed.Single(t => t.Id == first.Id).IsActive.Should().BeFalse();
        listed.Single(t => t.Id == second.Id).IsActive.Should().BeTrue();

        await using var db = _factory.CreateDbContext();
        var revokedEntity = await db.PositionApiTokens.SingleAsync(t => t.Id == first.Id);
        revokedEntity.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_is_idempotent_and_scoped_to_the_position()
    {
        var created = (await _service.GenerateAsync(_recruiter, _positionId, "only")).Value!;

        var again = await _service.RevokeAsync(_recruiter, _positionId, created.Id);
        var otherPosition = await _service.RevokeAsync(_recruiter, Guid.NewGuid(), created.Id);

        again.Succeeded.Should().BeTrue();
        otherPosition.Succeeded.Should().BeFalse();
        otherPosition.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Non_privileged_actors_are_rejected()
    {
        var candidate = new ActorContext(Guid.NewGuid(), IsAdmin: false, IsRecruiter: false, IsCandidate: true);

        (await _service.GenerateAsync(candidate, _positionId, null)).Error.Code
            .Should().Be(ErrorCodes.Forbidden);
        (await _service.ListAsync(candidate, _positionId)).Error.Code
            .Should().Be(ErrorCodes.Forbidden);
        (await _service.RevokeAsync(candidate, _positionId, Guid.NewGuid())).Error.Code
            .Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Unknown_revoked_or_foreign_hashes_do_not_authenticate()
    {
        var active = (await _service.GenerateAsync(_recruiter, _positionId, "active")).Value!;
        var toRevoke = (await _service.GenerateAsync(_recruiter, _positionId, "revoked")).Value!;
        (await _service.RevokeAsync(_recruiter, _positionId, toRevoke.Id)).Succeeded.Should().BeTrue();

        await using var db = _factory.CreateDbContext();
        var activeHash = PositionApiTokenSecret.Hash(active.Token);
        var revokedHash = PositionApiTokenSecret.Hash(toRevoke.Token);

        var matches = await db.PositionApiTokens.AsNoTracking()
            .Where(t => t.RevokedAt == null &&
                        (t.TokenHash == activeHash || t.TokenHash == revokedHash))
            .Select(t => t.TokenHash)
            .ToListAsync();
        matches.Should().ContainSingle().Which.Should().Be(activeHash);
    }
}
