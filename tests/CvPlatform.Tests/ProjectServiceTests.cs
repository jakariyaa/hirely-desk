using CvPlatform.Application.Common;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Projects;
using CvPlatform.Core.Data;
using CvPlatform.Infrastructure.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class ProjectServiceTests
{
    private static ActorContext Actor(Guid userId) => new(userId, false);
    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static IAppDbContextFactory CreateFactory(out AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        db = factory.CreateDbContext();
        return new TestFactory(factory);
    }

    [Fact]
    public async Task CreateAsync_creates_profile_and_project()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var userId = Guid.NewGuid();
        var input = new ProjectInput("Alpha", null, null, "# Hello", [" b ", "a", "a"]);

        var result = await service.CreateAsync(Actor(userId), input);

        result.Succeeded.Should().BeTrue();
        var profileId = db.Profiles.Single(p => p.UserId == userId).Id;
        db.Projects.Should().ContainSingle(p => p.Name == "Alpha" && p.ProfileId == profileId);
        db.ProjectTags.Select(t => t.Name).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task CreateAsync_rejects_blank_name()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);

        var result = await service.CreateAsync(Actor(Guid.NewGuid()), new ProjectInput("   ", null, null, "", []));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task CreateAsync_rejects_period_end_before_start()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);

        var result = await service.CreateAsync(Actor(Guid.NewGuid()), new ProjectInput(
            "Alpha", new DateOnly(2024, 5, 1), new DateOnly(2024, 1, 1), "", []));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ListForProfileAsync_is_paged_and_ordered()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var userId = Guid.NewGuid();
        foreach (var name in new[] { "C", "A", "B" })
            await service.CreateAsync(Actor(userId), new ProjectInput(name, null, null, "", Array.Empty<string>()));

        var page1 = await service.ListForProfileAsync(Actor(userId), userId, new PageRequest(1, 2));
        var page2 = await service.ListForProfileAsync(Actor(userId), userId, new PageRequest(2, 2));

        page1.Value.Should().NotBeNull();
        page1.Value!.TotalCount.Should().Be(3);
        page1.Value.Items.Select(p => p.Name).Should().Equal("A", "B");
        page2.Value!.Items.Select(p => p.Name).Should().Equal("C");
    }

    [Fact]
    public async Task GetAsync_denies_access_to_other_users_projects()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var ownerId = Guid.NewGuid();
        var created = await service.CreateAsync(Actor(ownerId), new ProjectInput("Secret", null, null, "", []));

        var result = await service.GetAsync(Actor(Guid.NewGuid()), created.Value!.Id);

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_swaps_tags_and_updates_fields()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var userId = Guid.NewGuid();
        var created = await service.CreateAsync(Actor(userId), new ProjectInput(
            "Alpha", new DateOnly(2023, 1, 1), new DateOnly(2023, 6, 30), "old", ["a", "b"]));

        var result = await service.UpdateAsync(Actor(userId), created.Value!.Id, new ProjectInput(
            "Alpha 2", new DateOnly(2023, 2, 1), new DateOnly(2023, 7, 31), "new", ["b", "c"],
            created.Value.Version));

        result.Succeeded.Should().BeTrue();
        db.ChangeTracker.Clear();
        db.Projects.Should().ContainSingle(p =>
            p.Name == "Alpha 2" && p.DescriptionMarkdown == "new");
        db.Projects.Include(p => p.Tags).Single().Tags.Select(t => t.Name).Should().BeEquivalentTo(["b", "c"]);
        result.Value!.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DeleteAsync_removes_only_own_project()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var ownerId = Guid.NewGuid();
        var created = await service.CreateAsync(Actor(ownerId), new ProjectInput("Alpha", null, null, "", []));

        var wrongUser = await service.DeleteAsync(Actor(Guid.NewGuid()), created.Value!.Id);
        wrongUser.Succeeded.Should().BeFalse();

        var owner = await service.DeleteAsync(Actor(ownerId), created.Value!.Id);
        owner.Succeeded.Should().BeTrue();
        db.Projects.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_rejects_stale_version()
    {
        var factory = CreateFactory(out var db);
        var service = new ProjectService(factory);
        var userId = Guid.NewGuid();
        var created = await service.CreateAsync(Actor(userId), new ProjectInput("Alpha", null, null, "", []));

        var result = await service.UpdateAsync(
            Actor(userId),
            created.Value!.Id,
            new ProjectInput("Changed", null, null, "", [], 0));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        db.Projects.Single().Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task ListForProfileAsync_rejects_different_actor()
    {
        var factory = CreateFactory(out _);
        var service = new ProjectService(factory);

        var result = await service.ListForProfileAsync(
            Actor(Guid.NewGuid()), Guid.NewGuid(), new PageRequest());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Admin_can_read_another_users_project()
    {
        var factory = CreateFactory(out _);
        var service = new ProjectService(factory);
        var owner = Guid.NewGuid();
        var created = await service.CreateAsync(
            Actor(owner), new ProjectInput("Secret", null, null, "", []));

        var result = await service.GetAsync(
            new ActorContext(Guid.NewGuid(), true), created.Value!.Id);

        result.Succeeded.Should().BeTrue();
    }
}
