using AwesomeAssertions;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Likes;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionStatsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public PositionStatsTests()
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
    public async Task List_and_browse_return_cv_and_like_counts()
    {
        var access = new PositionAccessService(_factory, new AccessRuleEngine());
        var positions = new PositionService(_factory);
        var cvs = new CvService(_factory, access);
        var likes = new LikeService(_factory, access);

        var recruiterId = Guid.NewGuid();
        PositionDto created;
        await using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = recruiterId, UserName = "recruiter" });
            await db.SaveChangesAsync();
        }
        created = (await positions.CreateAsync(new ActorContext(recruiterId, false, true),
            new PositionInput("Lead", "Desc", null, null, true))).Value!;

        var candidateId = await SeedCandidateAsync(_factory);
        var cvId = (await cvs.CreateAsync(new ActorContext(candidateId, false), created.Id)).Value!.Id;
        await using (var db = _factory.CreateDbContext())
        {
            var version = (await db.Cvs.SingleAsync(c => c.Id == cvId)).Version;
            (await cvs.PublishAsync(new ActorContext(candidateId, false), cvId, new CvStatusInput(version)))
                .Succeeded.Should().BeTrue();
        }
        (await likes.ToggleAsync(new ActorContext(recruiterId, false, true), cvId)).Succeeded.Should().BeTrue();

        var listed = await positions.ListAsync(new ActorContext(recruiterId, false, true), new PageRequest(1, 10));
        listed.Value!.Items.Should().ContainSingle()
            .Which.Should().Match<PositionDto>(p => p.CvCount == 1 && p.LikeCount == 1);

        var browsed = await access.BrowseAsync(new ActorContext(candidateId, false), new PageRequest(1, 10));
        browsed.Value!.Items.Should().ContainSingle()
            .Which.Should().Match<PositionDto>(p => p.CvCount == 1 && p.LikeCount == 1);
    }

    private static async Task<Guid> SeedCandidateAsync(IAppDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"c{userId:N}@example.test" });
        db.Profiles.Add(new Profile { Id = Guid.NewGuid(), UserId = userId });
        await db.SaveChangesAsync();
        return userId;
    }
}
