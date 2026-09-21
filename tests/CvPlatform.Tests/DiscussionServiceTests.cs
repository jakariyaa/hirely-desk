using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Discussions;
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

public class DiscussionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public DiscussionServiceTests()
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

    private sealed class SpyNotifier : IDiscussionNotifier
    {
        public readonly List<(Guid PositionId, DiscussionPostDto Post)> Calls = [];
        public event Action<Guid, DiscussionPostDto>? PostAdded;
        public void Notify(Guid positionId, DiscussionPostDto post)
        {
            Calls.Add((positionId, post));
            PostAdded?.Invoke(positionId, post);
        }
        public Task NotifyAsync(Guid positionId, DiscussionPostDto post, CancellationToken ct = default)
        {
            Notify(positionId, post);
            return Task.CompletedTask;
        }
    }

    private static DiscussionService Service(IAppDbContextFactory factory, IDiscussionNotifier notifier) =>
        new(factory, new PositionAccessService(factory, new AccessRuleEngine()), notifier);

    [Fact]
    public async Task Add_saves_then_notifies_and_list_returns_ordered()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var userId = await SeedUserWithoutProfileAsync(_factory);
        var actor = new ActorContext(userId, false, true);
        var spy = new SpyNotifier();

        var first = await Service(_factory, spy).AddAsync(actor, positionId, new DiscussionPostInput("Hello"));
        first.Succeeded.Should().BeTrue();
        spy.Calls.Should().ContainSingle().Which.PositionId.Should().Be(positionId);

        var second = await Service(_factory, spy).AddAsync(actor, positionId, new DiscussionPostInput("World"));
        second.Succeeded.Should().BeTrue();

        var list = await Service(_factory, spy).ListAsync(actor, positionId, new PageRequest(1, 20));
        list.Succeeded.Should().BeTrue();
        list.Value!.Items.Select(p => p.TextMarkdown).Should().ContainInOrder("Hello", "World");
    }

    [Fact]
    public async Task Add_rejects_empty_and_forbidden_position()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var userId = await SeedUserWithoutProfileAsync(_factory);
        var empty = await Service(_factory, new SpyNotifier())
            .AddAsync(new ActorContext(userId, false, true), positionId, new DiscussionPostInput("  "));
        empty.Error.Code.Should().Be(ErrorCodes.ValidationFailed);

        var defId = Guid.NewGuid();
        await SeedDefinitionAsync(_factory, defId, "City", AttributeDataType.String);
        var restrictedId = await SeedPositionAsync(_factory, isPublic: false,
            attributes: [(defId, "City", AttributeDataType.String, true)],
            rules: [(defId, RuleOperator.Equals, "Warsaw")]);
        var denied = await Service(_factory, new SpyNotifier())
            .AddAsync(new ActorContext(userId, false, false, true), restrictedId, new DiscussionPostInput("Hi"));
        denied.Error.Code.Should().Be(ErrorCodes.Forbidden);

        var list = await Service(_factory, new SpyNotifier())
            .ListAsync(new ActorContext(userId, false, false, true), restrictedId, new PageRequest(1, 20));
        list.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Candidate_can_post_to_an_accessible_position()
    {
        var positionId = await SeedPositionAsync(_factory, isPublic: true);
        var candidateId = await SeedUserWithoutProfileAsync(_factory);

        var result = await Service(_factory, new SpyNotifier()).AddAsync(
            new ActorContext(candidateId, false, false, true), positionId,
            new DiscussionPostInput("I am interested"));

        result.Succeeded.Should().BeTrue();
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
}
