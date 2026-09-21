using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Search;
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

public class SearchServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public SearchServiceTests()
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

    private static SearchService Service(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    private static CvService Cvs(IAppDbContextFactory factory) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()));

    [Fact]
    public async Task Empty_term_returns_empty()
    {
        var result = await Service(_factory).SearchAsync(new ActorContext(Guid.NewGuid(), false), "  ");
        result.Succeeded.Should().BeTrue();
        result.Value!.Positions.TotalCount.Should().Be(0);
        result.Value.Cvs.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Candidate_sees_only_own_cvs()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var positionId = await SeedPositionAsync(_factory, "Backend Engineer", isPublic: true,
            attributes: [(defId, "City", AttributeDataType.String, true)]);
        var candidateA = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var candidateB = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        await CreatePublishedCvAsync(candidateA, positionId);
        await CreatePublishedCvAsync(candidateB, positionId);

        var result = await Service(_factory).SearchAsync(new ActorContext(candidateA, false), "Warsaw");
        result.Succeeded.Should().BeTrue();
        result.Value!.Cvs.Items.Should().ContainSingle();
        result.Value.Cvs.Items.Single().CandidateName.Should().Contain(candidateA.ToString("N")[..6]);
    }

    [Fact]
    public async Task Recruiter_search_can_manage_all_positions_and_published_cvs()
    {
        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var positionId = await SeedPositionAsync(_factory, "Backend Engineer", isPublic: false,
            attributes: [(defId, "City", AttributeDataType.String, true)],
            rules: [(defId, RuleOperator.Equals, "Warsaw")]);
        var candidateId = await SeedCandidateAsync(_factory, ("City", AttributeDataType.String, "Warsaw", defId));
        var cvId = await CreatePublishedCvAsync(candidateId, positionId);
        var recruiterId = await SeedUserWithoutProfileAsync(_factory);
        await using (var db = _factory.CreateDbContext())
        {
            db.Positions.Where(p => p.Id == positionId).ExecuteUpdate(s => s.SetProperty(p => p.OwnerId, recruiterId));
        }

        var visible = await Service(_factory).SearchAsync(new ActorContext(recruiterId, false, true), "Warsaw");
        visible.Value!.Cvs.Items.Select(c => c.CvId).Should().Contain(cvId);

        await using (var db = _factory.CreateDbContext())
        {
            var profileId = await db.Profiles.Where(p => p.UserId == candidateId).Select(p => p.Id).SingleAsync();
            var value = await db.ProfileAttributeValues.SingleAsync(
                v => v.ProfileId == profileId && v.AttributeDefinitionId == defId);
            db.ProfileAttributeValues.Remove(value);
            await db.SaveChangesAsync();
        }

        var hidden = await Service(_factory).SearchAsync(new ActorContext(recruiterId, false, true), "Warsaw");
        hidden.Value!.Cvs.Items.Select(c => c.CvId).Should().Contain(cvId);
    }

    [Fact]
    public async Task Admin_sees_all_matching_positions()
    {
        await SeedPositionAsync(_factory, "Backend Engineer", isPublic: false);
        var result = await Service(_factory).SearchAsync(new ActorContext(Guid.NewGuid(), true), "Backend");
        result.Value!.Positions.TotalCount.Should().Be(1);
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
        return created.Value.Id;
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
        IAppDbContextFactory factory, string title, bool isPublic,
        List<(Guid DefinitionId, string Name, AttributeDataType Type, bool IsRequired)>? attributes = null,
        List<(Guid DefinitionId, RuleOperator Op, string Compare)>? rules = null)
    {
        await using var db = factory.CreateDbContext();
        var positionId = Guid.NewGuid();
        var position = new Position { Id = positionId, Title = title, IsPublic = isPublic };
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
