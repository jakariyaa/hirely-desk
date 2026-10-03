using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Integration;
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
        var created = await service.CreateAsync(new ActorContext(ownerId, false, IsRecruiter: true),
            new PositionInput("Lead", "Desc", null, null, true));
        created.Succeeded.Should().BeTrue($"create failed: {created.Error.Code} {created.Error.Message}");

        var stale = await service.UpdateAsync(new ActorContext(ownerId, false, IsRecruiter: true), created.Value!.Id,
            new PositionInput("Lead v2", "Desc", null, null, true, ExpectedVersion: created.Value.Version - 1));

        stale.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Aggregated_summary_translates_and_runs_on_postgres()
    {
        var ownerId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var yearsId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = ownerId, UserName = "owner" });
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "Experience" });
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = cityId, CategoryId = categoryId, Name = "City",
                DataType = Core.Enums.AttributeDataType.String,
            });
            db.AttributeDefinitions.Add(new AttributeDefinition
            {
                Id = yearsId, CategoryId = categoryId, Name = "Years",
                DataType = Core.Enums.AttributeDataType.Numeric,
            });
            db.Positions.Add(new Position
            {
                Id = positionId, OwnerId = ownerId, Title = "Backend Engineer", IsPublic = true,
                Attributes =
                [
                    new PositionAttribute { PositionId = positionId, AttributeDefinitionId = cityId, SortOrder = 0 },
                    new PositionAttribute { PositionId = positionId, AttributeDefinitionId = yearsId, SortOrder = 1 },
                ],
            });
            await db.SaveChangesAsync();

            foreach (var (city, years) in new[] { ("Warsaw", 2m), ("Warsaw", 6m), ("Krakow", 10m) })
            {
                var userId = Guid.NewGuid();
                var profileId = Guid.NewGuid();
                db.Users.Add(new ApplicationUser { Id = userId, UserName = $"u{userId:N}" });
                db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
                db.ProfileAttributeValues.Add(new ProfileAttributeValue
                {
                    Id = Guid.NewGuid(), ProfileId = profileId, AttributeDefinitionId = cityId,
                    StringValue = city,
                });
                db.ProfileAttributeValues.Add(new ProfileAttributeValue
                {
                    Id = Guid.NewGuid(), ProfileId = profileId, AttributeDefinitionId = yearsId,
                    NumericValue = years,
                });
                db.Cvs.Add(new Cv
                {
                    Id = Guid.NewGuid(), ProfileId = profileId, PositionId = positionId,
                    Status = Core.Enums.CvStatus.Draft,
                });
            }
            await db.SaveChangesAsync();
        }

        var service = new PositionSummaryService(_factory);
        var result = await service.GetSummaryAsync(positionId);

        result.Succeeded.Should().BeTrue($"summary failed: {result.Error.Code} {result.Error.Message}");
        var cityAttribute = result.Value!.Attributes.Single(a => a.Name == "City");
        cityAttribute.FilledCount.Should().Be(3);
        cityAttribute.TopValues![0].Should().Be(new TopValueDto("Warsaw", 2));

        var yearsAttribute = result.Value.Attributes.Single(a => a.Name == "Years");
        yearsAttribute.Numeric!.Count.Should().Be(3);
        yearsAttribute.Numeric.Average.Should().Be(6m);
        yearsAttribute.Numeric.Min.Should().Be(2m);
        yearsAttribute.Numeric.Max.Should().Be(10m);
    }

    [Fact]
    public async Task Last_used_stamp_updates_translate_and_throttle_on_postgres()
    {
        var ownerId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = ownerId, UserName = "owner" });
            db.Positions.Add(new Position
            {
                Id = positionId, OwnerId = ownerId, Title = "Backend Engineer", IsPublic = true,
            });
            db.PositionApiTokens.Add(new PositionApiToken
            {
                Id = tokenId, PositionId = positionId, Name = "Odoo",
                TokenHash = PositionApiTokenSecret.Hash(PositionApiTokenSecret.Create()),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var service = new PositionSummaryService(_factory, clock);

        (await service.GetSummaryAsync(positionId, tokenId)).Succeeded.Should().BeTrue();
        await using (var db = _factory.CreateDbContext())
        {
            var stamp = await db.PositionApiTokens.SingleAsync(t => t.Id == tokenId);
            stamp.LastUsedAt.Should().Be(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        }

        clock.Now = clock.Now.AddMinutes(2);
        (await service.GetSummaryAsync(positionId, tokenId)).Succeeded.Should().BeTrue();
        await using (var db = _factory.CreateDbContext())
        {
            var stamp = await db.PositionApiTokens.SingleAsync(t => t.Id == tokenId);
            stamp.LastUsedAt.Should().Be(new DateTime(2026, 10, 5, 12, 2, 0, DateTimeKind.Utc));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
