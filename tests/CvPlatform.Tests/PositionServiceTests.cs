using AwesomeAssertions;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Attributes;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
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
            new ActorContext(owner, false, true),
            new PositionInput(" .NET Developer ", " Backend role ", "Acme", "Senior", true));

        result.Succeeded.Should().BeTrue();
        result.Value!.OwnerId.Should().Be(owner);
        result.Value.Title.Should().Be(".NET Developer");
        result.Value.Version.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_copies_attribute_requirements()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);

        var result = await service.CreateAsync(owner,
            new PositionInput("Developer", "", null, null, true, Attributes:
            [new AttributeRequirementInput(definition.Id, true, 0)]));

        result.Succeeded.Should().BeTrue();
        var detail = await service.GetForEditAsync(owner, result.Value!.Id);
        detail.Value!.Attributes.Should().ContainSingle(attribute =>
            attribute.AttributeDefinitionId == definition.Id && attribute.IsRequired && attribute.SortOrder == 0);
    }

    [Fact]
    public async Task ListAsync_returns_all_positions_to_recruiters()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        await service.CreateAsync(owner, new PositionInput("Private", "", null, null, false));
        await service.CreateAsync(new ActorContext(Guid.NewGuid(), false, true), new PositionInput("Other", "", null, null, true));

        var result = await service.ListAsync(owner, new PageRequest());

        result.Succeeded.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Select(p => p.Title).Should().Contain(["Private", "Other"]);
    }

    [Fact]
    public async Task UpdateAsync_rejects_stale_version()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Original", "", null, null, true));

        var result = await service.UpdateAsync(owner, created.Value!.Id,
            new PositionInput("Changed", "", null, null, true, ExpectedVersion: 0));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_allows_any_recruiter_to_edit_position()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Original", "", null, null, true));

        var result = await service.UpdateAsync(new ActorContext(Guid.NewGuid(), false, true), created.Value!.Id,
            new PositionInput("Changed", "", null, null, true, ExpectedVersion: created.Value.Version));

        result.Succeeded.Should().BeTrue();
        result.Value!.Title.Should().Be("Changed");
    }

    [Fact]
    public async Task DuplicateAsync_copies_attributes_rules_and_resets_version()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", "Acme", "Senior", true));
        var positionId = created.Value!.Id;
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);
        await service.UpdateAsync(owner, positionId,
            new PositionInput("Lead v2", "", "Acme", "Senior", true, ExpectedVersion: 1));
        await service.SaveAttributeAsync(owner, positionId,
            new PositionAttributeInput(definition.Id, IsRequired: true, ExpectedVersion: 2));
        await service.SaveRuleAsync(owner, positionId,
            new AccessRuleInput(definition.Id, RuleOperator.Equals, "Warsaw", ExpectedVersion: 3));

        var result = await service.DuplicateAsync(owner, positionId);

        result.Succeeded.Should().BeTrue();
        var copy = result.Value!;
        copy.Id.Should().NotBe(positionId);
        copy.OwnerId.Should().Be(owner.UserId);
        copy.Title.Should().Be("Lead v2 (copy)");
        copy.Version.Should().Be(1);
        var detail = await service.GetForEditAsync(owner, copy.Id);
        detail.Succeeded.Should().BeTrue();
        detail.Value!.Attributes.Should().ContainSingle(a => a.AttributeDefinitionId == definition.Id && a.IsRequired);
        detail.Value!.AccessRules.Should().ContainSingle(r => r.ComparisonValue == "Warsaw");
    }

    [Fact]
    public async Task DuplicateAsync_allows_non_owner_recruiter()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var created = await service.CreateAsync(
            new ActorContext(Guid.NewGuid(), false, true), new PositionInput("Lead", "", null, null, true));

        var result = await service.DuplicateAsync(new ActorContext(Guid.NewGuid(), false, true), created.Value!.Id);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAttributeAsync_appends_and_updates_in_place()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", null, null, true));
        var positionId = created.Value!.Id;
        var first = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);
        var second = await SeedDefinitionAsync(factory, "Experience", AttributeDataType.Numeric);

        var add = await service.SaveAttributeAsync(owner, positionId,
            new PositionAttributeInput(first.Id, IsRequired: false));
        add.Succeeded.Should().BeTrue();
        var versionAfterFirst = (await service.GetAsync(owner, positionId)).Value!.Version;

        var append = await service.SaveAttributeAsync(owner, positionId,
            new PositionAttributeInput(second.Id, IsRequired: true, ExpectedVersion: versionAfterFirst));
        append.Succeeded.Should().BeTrue();

        var detail = await service.GetForEditAsync(owner, positionId);
        detail.Value!.Attributes.Select(a => (a.AttributeDefinitionId, a.SortOrder)).Should().Equal(
            (first.Id, 0), (second.Id, 1));
    }

    [Fact]
    public async Task SaveAttributeAsync_upserts_same_attribute_in_place()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", null, null, true));
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);
        await service.SaveAttributeAsync(owner, created.Value!.Id,
            new PositionAttributeInput(definition.Id, IsRequired: false, ExpectedVersion: 1));

        var result = await service.SaveAttributeAsync(owner, created.Value.Id,
            new PositionAttributeInput(definition.Id, IsRequired: true, ExpectedVersion: 2));

        result.Succeeded.Should().BeTrue();
        var detail = await service.GetForEditAsync(owner, created.Value.Id);
        detail.Value!.Attributes.Should().ContainSingle(a =>
            a.AttributeDefinitionId == definition.Id && a.IsRequired && a.SortOrder == 0);
    }

    [Fact]
    public async Task RemoveAttributeAsync_also_removes_dependent_rules()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", null, null, true));
        var positionId = created.Value!.Id;
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);
        await service.SaveAttributeAsync(owner, positionId,
            new PositionAttributeInput(definition.Id, IsRequired: true, ExpectedVersion: 1));
        var version = (await service.GetAsync(owner, positionId)).Value!.Version;
        await service.SaveRuleAsync(owner, positionId,
            new AccessRuleInput(definition.Id, RuleOperator.Equals, "Warsaw", ExpectedVersion: version));
        version = (await service.GetAsync(owner, positionId)).Value!.Version;

        var result = await service.RemoveAttributeAsync(owner, positionId, definition.Id, version);

        result.Succeeded.Should().BeTrue();
        var detail = await service.GetForEditAsync(owner, positionId);
        detail.Value!.Attributes.Should().BeEmpty();
        detail.Value!.AccessRules.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveRuleAsync_deletes_only_target_rule()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", null, null, true));
        var positionId = created.Value!.Id;
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);
        await service.SaveAttributeAsync(owner, positionId,
            new PositionAttributeInput(definition.Id, IsRequired: false, ExpectedVersion: 1));
        var version = (await service.GetAsync(owner, positionId)).Value!.Version;
        await service.SaveRuleAsync(owner, positionId,
            new AccessRuleInput(definition.Id, RuleOperator.Equals, "Warsaw", ExpectedVersion: version));
        version = (await service.GetAsync(owner, positionId)).Value!.Version;
        await service.SaveRuleAsync(owner, positionId,
            new AccessRuleInput(definition.Id, RuleOperator.Equals, "Krakow", ExpectedVersion: version));
        version = (await service.GetAsync(owner, positionId)).Value!.Version;
        var detail = await service.GetForEditAsync(owner, positionId);
        var target = detail.Value!.AccessRules.Single(r => r.ComparisonValue == "Warsaw");

        var result = await service.RemoveRuleAsync(owner, positionId, target.Id, version);

        result.Succeeded.Should().BeTrue();
        var after = await service.GetForEditAsync(owner, positionId);
        after.Value!.AccessRules.Select(r => r.ComparisonValue).Should().Equal("Krakow");
    }

    [Fact]
    public async Task SaveAttributeAsync_rejects_stale_version()
    {
        var factory = CreateFactory();
        var service = new PositionService(factory);
        var owner = new ActorContext(Guid.NewGuid(), false, true);
        var created = await service.CreateAsync(owner, new PositionInput("Lead", "", null, null, true));
        var definition = await SeedDefinitionAsync(factory, "City", AttributeDataType.String);

        var result = await service.SaveAttributeAsync(owner, created.Value!.Id,
            new PositionAttributeInput(definition.Id, IsRequired: false, ExpectedVersion: 99));

        result.Succeeded.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    private static async Task<AttributeDefinitionAdminDto> SeedDefinitionAsync(
        IAppDbContextFactory factory, string name, AttributeDataType dataType)
    {
        await using var db = factory.CreateDbContext();
        var category = new AttributeCategory { Id = Guid.NewGuid(), Name = $"Cat-{name}" };
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = name,
            DataType = dataType,
        };
        db.AttributeCategories.Add(category);
        db.AttributeDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return new AttributeDefinitionAdminDto(
            definition.Id, category.Id, category.Name, name, null, dataType, false, null, 1);
    }
}
