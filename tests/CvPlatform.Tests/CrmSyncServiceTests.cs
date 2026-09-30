using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Crm;
using CvPlatform.Core.Crm;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Infrastructure.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class CrmSyncServiceTests
{
    private static ActorContext Actor(Guid userId, bool admin = false) => new(userId, admin);

    private sealed class TestFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
    {
        public IAppDbContext CreateDbContext() => factory.CreateDbContext();
    }

    private sealed class FakeCrmService(bool configured = true, CrmSyncRecord? record = null) : ICrmService
    {
        public bool IsConfigured => configured;
        public CrmAccountRequest? LastRequest { get; private set; }

        public Task<CrmSyncRecord?> CreateAccountWithContactAsync(
            CrmAccountRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(record ?? new CrmSyncRecord("acc-1", "con-1"));
        }
    }

    private sealed class ThrowingCrmService : ICrmService
    {
        public bool IsConfigured => true;

        public Task<CrmSyncRecord?> CreateAccountWithContactAsync(
            CrmAccountRequest request, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("boom");
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

    private static async Task<ApplicationUser> SeedUserAsync(AppDbContext db)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "jane@example.com",
            Profile = new Profile { Id = Guid.NewGuid() },
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static CrmSyncInput Input(string accountName = "Acme Corp") => new(
        accountName, "IT", null, null, "Engineer", null, "Warsaw", "Poland", null);

    [Fact]
    public async Task SyncAsync_forbidden_for_other_user()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        var service = new CrmSyncService(factory, new FakeCrmService());

        var result = await service.SyncAsync(Actor(Guid.NewGuid()), user.Id, Input());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task SyncAsync_fails_when_not_configured()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        var service = new CrmSyncService(factory, new FakeCrmService(configured: false));

        var result = await service.SyncAsync(Actor(user.Id), user.Id, Input());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ServiceUnavailable);
    }

    [Fact]
    public async Task SyncAsync_validates_account_name()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        var service = new CrmSyncService(factory, new FakeCrmService());

        var result = await service.SyncAsync(Actor(user.Id), user.Id, Input("  "));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task SyncAsync_conflicts_when_already_synced()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        user.SalesforceAccountId = "acc-existing";
        await db.SaveChangesAsync();
        var service = new CrmSyncService(factory, new FakeCrmService());

        var result = await service.SyncAsync(Actor(user.Id), user.Id, Input());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task SyncAsync_creates_records_and_persists_ids()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        var crm = new FakeCrmService();
        var service = new CrmSyncService(factory, crm);

        var result = await service.SyncAsync(Actor(user.Id), user.Id, Input());

        result.Succeeded.Should().BeTrue();
        result.Value.AccountId.Should().Be("acc-1");
        crm.LastRequest!.AccountName.Should().Be("Acme Corp");
        crm.LastRequest.ContactEmail.Should().Be("jane@example.com");
        var persisted = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        persisted.SalesforceAccountId.Should().Be("acc-1");
        persisted.SalesforceContactId.Should().Be("con-1");
    }

    [Fact]
    public async Task SyncAsync_maps_http_failure_to_service_unavailable()
    {
        var factory = CreateFactory(out var db);
        var user = await SeedUserAsync(db);
        var service = new CrmSyncService(factory, new ThrowingCrmService());

        var result = await service.SyncAsync(Actor(user.Id), user.Id, Input());

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ServiceUnavailable);
    }
}
