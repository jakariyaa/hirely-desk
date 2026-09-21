using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
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

public class LikeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public LikeServiceTests()
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

    private static LikeService Service(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    private static CvService Cvs(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    [Fact]
    public async Task Toggle_is_idempotent_and_counts_once()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var recruiterId = await SeedUserWithoutProfileAsync(_factory);
        var cvId = await CreatePublishedCvAsync(candidateId, positionId);
        var actor = new ActorContext(recruiterId, false, true);

        var like = await Service(_factory).ToggleAsync(actor, cvId);
        like.Succeeded.Should().BeTrue();
        like.Value!.LikedByMe.Should().BeTrue();
        like.Value.LikeCount.Should().Be(1);

        var unlike = await Service(_factory).ToggleAsync(actor, cvId);
        unlike.Succeeded.Should().BeTrue();
        unlike.Value!.LikedByMe.Should().BeFalse();
        unlike.Value.LikeCount.Should().Be(0);
    }

    [Fact]
    public async Task Toggle_forbids_self_like_and_draft_and_gated()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var restrictedId = await SeedPositionAsync(_factory, isPublic: false,
            attributes: [(defId, "City", AttributeDataType.String, true)],
            rules: [(defId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var cvId = await CreatePublishedCvAsync(candidateId, restrictedId);

        var self = await Service(_factory).ToggleAsync(new ActorContext(candidateId, false), cvId);
        self.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var draftPosition = await SeedPositionAsync(_factory, isPublic: true);
        var draftCv = await CreateDraftCvAsync(candidateId, draftPosition);
        var recruiter = await SeedUserWithoutProfileAsync(_factory);
        var draft = await Service(_factory).ToggleAsync(new ActorContext(recruiter, false, true), draftCv);
        draft.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var stranger = await SeedUserWithoutProfileAsync(_factory);
        await using (var db = _factory.CreateDbContext())
        {
            var profileId = await db.Profiles.Where(p => p.UserId == candidateId).Select(p => p.Id).SingleAsync();
            var value = await db.ProfileAttributeValues.SingleAsync(
                v => v.ProfileId == profileId && v.AttributeDefinitionId == defId);
            db.ProfileAttributeValues.Remove(value);
            await db.SaveChangesAsync();
        }

        var sharedRecruiter = await Service(_factory).ToggleAsync(new ActorContext(stranger, false, true), cvId);
        sharedRecruiter.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetCounts_batches_without_n_plus_one()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedCandidateAsync(_factory);
        var otherId = await SeedCandidateAsync(_factory);
        var cvA = await CreatePublishedCvAsync(candidateId, positionId);
        var cvB = await CreatePublishedCvAsync(otherId, positionId);
        var recruiter = await SeedUserWithoutProfileAsync(_factory);
        await Service(_factory).ToggleAsync(new ActorContext(recruiter, false, true), cvA);

        var counts = await Service(_factory).GetCountsAsync(
            new ActorContext(recruiter, false, true), [cvA, cvB, Guid.NewGuid()]);
        counts.Succeeded.Should().BeTrue();
        counts.Value![cvA].Should().Be(1);
        counts.Value.Should().NotContainKey(cvB);
    }

    private async Task<Guid> CreatePublishedCvAsync(Guid candidateId, Guid positionId)
    {
        var cvId = await CreateDraftCvAsync(candidateId, positionId);
        await using var db = _factory.CreateDbContext();
        var version = (await db.Cvs.SingleAsync(c => c.Id == cvId)).Version;
        var published = await Cvs(_factory).PublishAsync(
            new ActorContext(candidateId, false), cvId, new CvStatusInput(version));
        published.Succeeded.Should().BeTrue();
        return cvId;
    }

    private async Task<Guid> CreateDraftCvAsync(Guid candidateId, Guid positionId)
    {
        var result = await Cvs(_factory).CreateAsync(new ActorContext(candidateId, false), positionId);
        result.Succeeded.Should().BeTrue();
        return result.Value!.Id;
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
        IAppDbContextFactory factory, bool isPublic,
        List<(Guid DefinitionId, string Name, AttributeDataType Type, bool IsRequired)>? attributes = null,
        List<(Guid DefinitionId, RuleOperator Op, string Compare)>? rules = null)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position { Id = positionId, Title = "Lead", IsPublic = isPublic };
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
