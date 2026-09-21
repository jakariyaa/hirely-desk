using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Search;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CvPlatform.Tests;

public sealed class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("cvplatform_test")
        .WithUsername("cvplatform")
        .WithPassword("cvplatform")
        .Build();

    private IAppDbContextFactory _factory = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var services = new ServiceCollection();
        var connectionString = _container.GetConnectionString();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var relationalFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = relationalFactory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }
        _factory = new TestFactory(relationalFactory);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    [Fact]
    public async Task Full_text_search_matches_position_title()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Positions.Add(new Position { Id = Guid.NewGuid(), Title = "Backend Engineer", IsPublic = true });
            await db.SaveChangesAsync();
        }

        var service = new SearchService(_factory, new PositionAccessService(_factory, new AccessRuleEngine()));
        var result = await service.SearchAsync(new ActorContext(Guid.NewGuid(), true), "Backend");

        result.Value!.Positions.Items.Should().ContainSingle().Which.Title.Should().Be("Backend Engineer");
    }

    [Fact]
    public async Task Duplicate_cv_violates_unique_index()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "u" });
            db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
            db.Positions.Add(new Position { Id = positionId, Title = "Lead", IsPublic = true });
            db.Cvs.Add(new Cv
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                PositionId = positionId,
                Status = Core.Enums.CvStatus.Draft,
            });
            await db.SaveChangesAsync();

            db.Cvs.Add(new Cv
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                PositionId = positionId,
                Status = Core.Enums.CvStatus.Draft,
            });
            await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task Stale_version_update_conflicts()
    {
        var ownerId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = ownerId, UserName = "owner" });
            await db.SaveChangesAsync();
        }
        var service = new PositionService(_factory);
        var created = await service.CreateAsync(new ActorContext(ownerId, false),
            new PositionInput("Lead", "Desc", null, null, true));
        created.Succeeded.Should().BeTrue($"create failed: {created.Error.Code} {created.Error.Message}");

        var stale = await service.UpdateAsync(new ActorContext(ownerId, false), created.Value!.Id,
            new PositionInput("Lead v2", "Desc", null, null, true, ExpectedVersion: created.Value.Version - 1));

        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }
}
