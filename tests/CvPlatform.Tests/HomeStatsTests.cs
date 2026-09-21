using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Home;
using CvPlatform.Application.Likes;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class HomeStatsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public HomeStatsTests()
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
        }
        _factory = new TestFactory(relationalFactory);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static HomeStatsService Service(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    private static CvService Cvs(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    [Fact]
    public async Task TagCloud_counts_shared_tags()
    {
        var userId = await SeedCandidateAsync(_factory);
        await using (var db = _factory.CreateDbContext())
        {
            var profileId = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).SingleAsync();
            var blazor = new ProjectTag { Id = Guid.NewGuid(), Name = "blazor" };
            var dotnet = new ProjectTag { Id = Guid.NewGuid(), Name = "dotnet" };
            db.Projects.Add(new Project { Id = Guid.NewGuid(), ProfileId = profileId, Name = "A", Tags = [blazor, dotnet] });
            db.Projects.Add(new Project { Id = Guid.NewGuid(), ProfileId = profileId, Name = "B", Tags = [blazor] });
            await db.SaveChangesAsync();
        }

        var stats = await Service(_factory).GetAsync(new ActorContext(userId, false));

        stats.Value!.TagCloud.Should().ContainSingle(t => t.Tag == "blazor").Which.Count.Should().Be(2);
        stats.Value.TagCloud.Should().ContainSingle(t => t.Tag == "dotnet").Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task Popular_orders_by_cv_then_like_count()
    {
        var recruiter = await SeedUserWithoutProfileAsync(_factory);
        var alpha = await SeedPositionAsync(_factory, "Alpha", isPublic: true, ownerId: recruiter);
        var beta = await SeedPositionAsync(_factory, "Beta", isPublic: true, ownerId: recruiter);
        var c1 = await SeedCandidateAsync(_factory);
        var c2 = await SeedCandidateAsync(_factory);
        var cvAlpha1 = await CreatePublishedCvAsync(c1, alpha);
        await CreatePublishedCvAsync(c2, alpha);
        await CreatePublishedCvAsync(c1, beta);

        var likeService = new LikeService(_factory, new PositionAccessService(_factory, new AccessRuleEngine()));
        await likeService.ToggleAsync(new ActorContext(recruiter, false, true), cvAlpha1);

        var stats = await Service(_factory).GetAsync(new ActorContext(recruiter, false, true));
        stats.Succeeded.Should().BeTrue();
        stats.Value!.PopularPositions.Select(p => p.PositionId).Should().ContainInOrder(alpha, beta);
        stats.Value.PopularPositions.First().CvCount.Should().Be(2);
    }

    [Fact]
    public async Task Latest_respects_visibility()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var restricted = await SeedPositionAsync(_factory, "Restricted", isPublic: false,
            attributes: [(defId, "City", AttributeDataType.String, true)],
            rules: [(defId, RuleOperator.Equals, "Warsaw")]);
        var candidate = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        await CreatePublishedCvAsync(candidate, restricted);

        var stranger = await SeedUserWithoutProfileAsync(_factory);
        var stats = await Service(_factory).GetAsync(new ActorContext(stranger, false));
        stats.Value!.LatestCvs.Should().BeEmpty();

        var owner = await Service(_factory).GetAsync(new ActorContext(candidate, false));
        owner.Value!.LatestCvs.Should().ContainSingle();
    }

    [Fact]
    public async Task Public_stats_include_only_public_published_activity()
    {
        var recruiter = await SeedUserWithoutProfileAsync(_factory);
        var publicPosition = await SeedPositionAsync(_factory, "Public", isPublic: true, ownerId: recruiter);
        var restrictedPosition = await SeedPositionAsync(_factory, "Restricted", isPublic: false, ownerId: recruiter);
        var candidate = await SeedCandidateAsync(_factory);
        var publicCv = await CreatePublishedCvAsync(candidate, publicPosition);

        await using (var db = _factory.CreateDbContext())
        {
            var cv = await db.Cvs.SingleAsync(c => c.Id == publicCv);
            cv.CreatedAt = DateTime.UtcNow.AddHours(-1);
            var profileId = await db.Profiles.Where(p => p.UserId == candidate).Select(p => p.Id).SingleAsync();
            db.Cvs.Add(new Cv
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                PositionId = restrictedPosition,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Status = CvStatus.Published,
                PublishedAt = DateTime.UtcNow.AddHours(-1),
            });
            await db.SaveChangesAsync();
        }

        var stats = await Service(_factory).GetPublicAsync();

        stats.Succeeded.Should().BeTrue();
        stats.Value!.PublicPositionCount.Should().Be(1);
        stats.Value.PublishedCvCount.Should().Be(1);
        stats.Value.NewPublishedCvsLast24Hours.Should().Be(1);
    }

    private async Task<Guid> CreatePublishedCvAsync(Guid candidateId, Guid positionId)
    {
        var created = await Cvs(_factory).CreateAsync(new ActorContext(candidateId, false), positionId);
        created.Succeeded.Should().BeTrue();
        await using var db = _factory.CreateDbContext();
        var version = (await db.Cvs.SingleAsync(c => c.Id == created.Value!.Id)).Version;
        var published = await Cvs(_factory).PublishAsync(
            new ActorContext(candidateId, false), created.Value!.Id, new CvStatusInput(version));
        published.Succeeded.Should().BeTrue();
        return created.Value!.Id;
    }

    private static async Task<Guid> SeedUserWithoutProfileAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"u{userId:N}@example.test" });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task SeedDefinitionAsync(
        IAppDbContextFactory factory, Guid definitionId, string name, AttributeDataType type)
    {
        await using var db = factory.CreateDbContext();
        db.AttributeCategories.Add(new AttributeCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            Definitions = [new AttributeDefinition { Id = definitionId, Name = name, DataType = type }],
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPositionAsync(
        IAppDbContextFactory factory, string title, bool isPublic, Guid? ownerId = null,
        List<(Guid DefinitionId, string Name, AttributeDataType Type, bool IsRequired)>? attributes = null,
        List<(Guid DefinitionId, RuleOperator Op, string Compare)>? rules = null)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position { Id = positionId, Title = title, IsPublic = isPublic, OwnerId = ownerId };
        foreach (var (definitionId, _, _, isRequired) in attributes ?? [])
            position.Attributes.Add(new PositionAttribute
            {
                PositionId = positionId,
                AttributeDefinitionId = definitionId,
                IsRequired = isRequired,
                SortOrder = position.Attributes.Count,
            });
        foreach (var (definitionId, op, compare) in rules ?? [])
            position.AccessRules.Add(new AccessRule
            {
                Id = Guid.NewGuid(),
                AttributeDefinitionId = definitionId,
                DataType = AttributeDataType.String,
                Operator = op,
                ComparisonValue = compare,
            });
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return positionId;
    }

    private static async Task<Guid> SeedCandidateAsync(
        IAppDbContextFactory factory,
        params (string Name, AttributeDataType Type, string? Value, Guid DefinitionId)[] values)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
        foreach (var (name, type, value, definitionId) in values)
        {
            if (!await db.AttributeDefinitions.AnyAsync(d => d.Id == definitionId))
                db.AttributeCategories.Add(new AttributeCategory
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Definitions = [new AttributeDefinition { Id = definitionId, Name = name, DataType = type }],
                });
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = definitionId,
                StringValue = value,
            });
        }
        await db.SaveChangesAsync();
        return userId;
    }
}
