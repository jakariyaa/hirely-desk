using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Data;
using CvPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class PositionServiceTests
{
    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private static IAppDbContextFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        return new TestFactory(services.BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>());
    }

    [Fact]
    public async Task AssignOwnerAsync_allows_admin_to_claim_orphan_position()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new VersionIncrementInterceptor()));
        var provider = services.BuildServiceProvider();
        var efFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var ownerId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        await using (var db = efFactory.CreateDbContext())
        {
            db.Users.Add(new CvPlatform.Core.Entities.ApplicationUser { Id = ownerId, UserName = "owner" });
            db.Positions.Add(new CvPlatform.Core.Entities.Position { Id = positionId, Title = "Orphan" });
            await db.SaveChangesAsync();
        }

        var result = await new PositionService(new TestFactory(efFactory)).AssignOwnerAsync(
            new ActorContext(Guid.NewGuid(), true), positionId, ownerId, 1);

        result.Succeeded.Should().BeTrue();
        result.Value!.OwnerId.Should().Be(ownerId);
        result.Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_assigns_owner_and_version()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = Guid.NewGuid();

        var result = await service.CreateAsync(
            new ActorContext(owner, false),
            new PositionInput(" .NET Developer ", " Backend role ", "Acme", "Senior", true));

        result.Succeeded.Should().BeTrue();
        result.Value!.OwnerId.Should().Be(owner);
        result.Value.Title.Should().Be(".NET Developer");
        result.Value.Version.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_hides_other_owners_positions()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false);
        await service.CreateAsync(owner, new PositionInput("Private", "", null, null, false));
        await service.CreateAsync(new ActorContext(Guid.NewGuid(), false), new PositionInput("Other", "", null, null, true));

        var result = await service.ListAsync(owner, new PageRequest());

        result.Succeeded.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().Title.Should().Be("Private");
    }

    [Fact]
    public async Task UpdateAsync_rejects_stale_version()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false);
        var created = await service.CreateAsync(owner, new PositionInput("Original", "", null, null, true));

        var result = await service.UpdateAsync(owner, created.Value!.Id,
            new PositionInput("Changed", "", null, null, true, ExpectedVersion: 0));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_allows_admin_to_edit_position()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false);
        var created = await service.CreateAsync(owner, new PositionInput("Original", "", null, null, true));

        var result = await service.UpdateAsync(new ActorContext(Guid.NewGuid(), true), created.Value!.Id,
            new PositionInput("Changed", "", null, null, true, ExpectedVersion: created.Value.Version));

        result.Succeeded.Should().BeTrue();
        result.Value!.Title.Should().Be("Changed");
    }
}
