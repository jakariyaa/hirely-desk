using AwesomeAssertions;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
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

public class DropdownLifecycleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IAppDbContextFactory _factory;

    public DropdownLifecycleTests()
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
    public async Task Removing_referenced_option_requires_force()
    {
        var service = new AttributeDefinitionService(_factory);
        var admin = new ActorContext(Guid.NewGuid(), true);
        Guid categoryId;
        await using (var db = _factory.CreateDbContext())
        {
            categoryId = Guid.NewGuid();
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "Langs" });
            await db.SaveChangesAsync();
        }
        var created = await service.CreateAsync(admin, new AttributeDefinitionInput(
            categoryId, "Level", null, AttributeDataType.Dropdown, """{"choices":["A","B"]}"""));
        created.Succeeded.Should().BeTrue();
        var id = created.Value!.Id;

        await using (var db = _factory.CreateDbContext())
        {
            var userId = Guid.NewGuid();
            var profileId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "u" });
            db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = id,
                DropdownOption = "A",
            });
            await db.SaveChangesAsync();
        }

        var blocked = await service.UpdateAsync(admin, id, new AttributeDefinitionInput(
            categoryId, "Level", null, AttributeDataType.Dropdown, """{"choices":["B"]}""",
            created.Value.Version));
        blocked.Succeeded.Should().BeFalse();
        blocked.Error.Code.Should().Be(ErrorCodes.Conflict);

        var impact = await service.GetOptionChangeImpactAsync(admin, id, new AttributeDefinitionInput(
            categoryId, "Level", null, AttributeDataType.Dropdown, """{"choices":["B"]}"""));
        impact.Value!.RemovedOptions.Should().Contain("A");
        impact.Value.ProfileValues.Should().Be(1);

        var current = await service.ListAsync(admin, new AttributeCatalogQuery(Search: "Level"));
        var version = current.Value!.Items.Single().Version;
        var forced = await service.UpdateAsync(admin, id, new AttributeDefinitionInput(
            categoryId, "Level", null, AttributeDataType.Dropdown, """{"choices":["B"]}""",
            version, ForceOptionRemoval: true));
        forced.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Recruiter_cannot_remove_shared_dropdown_options()
    {
        var service = new AttributeDefinitionService(_factory);
        var admin = new ActorContext(Guid.NewGuid(), true);
        var recruiter = new ActorContext(Guid.NewGuid(), false, true);
        Guid categoryId;
        await using (var db = _factory.CreateDbContext())
        {
            categoryId = Guid.NewGuid();
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "Languages" });
            await db.SaveChangesAsync();
        }

        var created = await service.CreateAsync(admin, new AttributeDefinitionInput(
            categoryId, "Level", null, AttributeDataType.Dropdown, "{\"choices\":[\"A\",\"B\"]}"));
        var result = await service.UpdateAsync(recruiter, created.Value!.Id,
            new AttributeDefinitionInput(
                categoryId, "Level", null, AttributeDataType.Dropdown, "{\"choices\":[\"B\"]}",
                created.Value.Version));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Attribute_type_cannot_change_when_values_exist()
    {
        var service = new AttributeDefinitionService(_factory);
        var admin = new ActorContext(Guid.NewGuid(), true);
        Guid categoryId;
        await using (var db = _factory.CreateDbContext())
        {
            categoryId = Guid.NewGuid();
            db.AttributeCategories.Add(new AttributeCategory { Id = categoryId, Name = "Profile" });
            await db.SaveChangesAsync();
        }

        var created = await service.CreateAsync(admin, new AttributeDefinitionInput(
            categoryId, "City", null, AttributeDataType.String, null));
        var profileId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var userId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "type-change" });
            db.Profiles.Add(new Profile { Id = profileId, UserId = userId });
            db.ProfileAttributeValues.Add(new ProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = created.Value!.Id,
                StringValue = "Warsaw",
            });
            await db.SaveChangesAsync();
        }

        var result = await service.UpdateAsync(admin, created.Value!.Id,
            new AttributeDefinitionInput(
                categoryId, "City", null, AttributeDataType.Text, null,
                created.Value.Version));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Conflict);
    }
}
