using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Badges;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class BadgeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public BadgeServiceTests()
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

    [Fact]
    public async Task Badges_are_earned_as_activity_grows()
    {
        var service = new BadgeService(_factory);
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var actor = new ActorContext(userId, false);
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "u" });
            db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
            await db.SaveChangesAsync();
        }

        (await service.GetForUserAsync(actor, userId)).Value!.Should().BeEmpty();

        var definitionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.AttributeCategories.Add(new AttributeCategory
            {
                Id = Guid.NewGuid(),
                Name = "Skills",
                Definitions = [new AttributeDefinition { Id = definitionId, Name = "Skill", DataType = AttributeDataType.String }],
            });
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = definitionId,
                StringValue = "C#",
            });
            var positionId = Guid.NewGuid();
            db.Positions.Add(new Position { Id = positionId, Title = "Lead", IsPublic = true, OwnerId = userId });
            var cvId = Guid.NewGuid();
            var recruiterId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser { Id = recruiterId, UserName = "recruiter" });
            db.Cvs.Add(new Cv
            {
                Id = cvId,
                ProfileId = profileId,
                PositionId = positionId,
                Status = CvStatus.Published,
                PublishedAt = DateTime.UtcNow,
            });
            db.CvLikes.Add(new CvLike { CvId = cvId, RecruiterId = recruiterId });
            await db.SaveChangesAsync();
        }

        var badges = (await service.GetForUserAsync(actor, userId)).Value!;
        badges.Select(b => b.Key).Should().BeEquivalentTo(
            ["ProfileStarter", "CvCreator", "Publisher", "LikedAuthor", "Recruiter"]);
    }

    [Fact]
    public async Task Cannot_view_other_users_badges()
    {
        var result = await new BadgeService(_factory)
            .GetForUserAsync(new ActorContext(Guid.NewGuid(), false), Guid.NewGuid());
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }
}
